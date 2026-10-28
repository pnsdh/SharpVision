#!/usr/bin/env bash
# Moves external/EXDSchema to the NEWEST commit on xivdev/EXDSchema's "latest" branch
# that the project still builds with. Tries the tip first, then walks back one commit
# at a time, so a single broken upstream commit (e.g. a schema typo the Lumina.Excel
# generator rejects) only holds back the commits from it onward instead of blocking
# every update. Once upstream fixes it, the next run reaches the tip again.
#
# Run from the repository root. Writes to $GITHUB_OUTPUT (if set):
#   changed=true|false   whether the submodule moved
#   skipped=<n>          newer commits held back because they don't build
# Exits non-zero only if none of the new commits builds.
set -euo pipefail

sub=external/EXDSchema
max=${MAX_CANDIDATES:-20}
out=${GITHUB_OUTPUT:-/dev/null}

# actions/checkout fetches submodules shallow at the pinned commit with no branch refs;
# walking back from the tip to our pin needs the branch history.
if [ "$(git -C "$sub" rev-parse --is-shallow-repository)" = true ]; then
  git -C "$sub" fetch -q --no-tags --unshallow origin +refs/heads/latest:refs/remotes/origin/latest
else
  git -C "$sub" fetch -q --no-tags origin +refs/heads/latest:refs/remotes/origin/latest
fi

pinned=$(git -C "$sub" rev-parse HEAD)
mapfile -t candidates < <(git -C "$sub" rev-list --max-count="$max" "$pinned..origin/latest")

if [ "${#candidates[@]}" -eq 0 ]; then
  echo "EXDSchema is already up to date."
  echo "changed=false" >> "$out"
  exit 0
fi
echo "${#candidates[@]} new EXDSchema commit(s) since $(git -C "$sub" rev-parse --short "$pinned")."

for i in "${!candidates[@]}"; do
  c=${candidates[$i]}
  git -C "$sub" checkout -q "$c"
  desc=$(git -C "$sub" log -1 --format='%h (%cs) %s')
  echo "::group::Build with EXDSchema $desc"
  if dotnet build SharpVision.csproj -c Release -nologo; then
    echo "::endgroup::"
    echo "Now at $desc"
    if [ "$i" -gt 0 ]; then
      echo "::warning::Newest EXDSchema commit $(git -C "$sub" rev-parse --short "${candidates[0]}") does not build; using $(git -C "$sub" rev-parse --short "$c") instead. $i newer commit(s) held back until fixed upstream."
    fi
    echo "changed=true" >> "$out"
    echo "skipped=$i" >> "$out"
    exit 0
  fi
  echo "::endgroup::"
  echo "EXDSchema $desc does not build; trying the previous commit."
done

git -C "$sub" checkout -q "$pinned"
echo "::error::None of the ${#candidates[@]} newest EXDSchema commits builds (the Lumina.Excel generator may need an update). Staying at $(git -C "$sub" rev-parse --short "$pinned")."
exit 1
