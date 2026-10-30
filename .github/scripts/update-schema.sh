#!/usr/bin/env bash
# Moves external/EXDSchema to the NEWEST commit on xivdev/EXDSchema's "latest" branch
# that the project still builds with. Tries the tip first, then walks back one commit
# at a time, so a single broken upstream commit (e.g. a schema typo the Lumina.Excel
# generator rejects) only holds back the commits from it onward instead of blocking
# every update. Once upstream fixes it, the next run reaches the tip again.
#
# Held-back commits are NOT a failure (they'd fail the run every day until upstream
# fixes them); they're reported via outputs + a markdown summary, which the workflow
# turns into a single tracking issue.
#
# Run from the repository root. Writes to $GITHUB_OUTPUT (if set):
#   changed=true|false   whether the submodule moved
#   skipped=<n>          newer commits held back because they don't build
#   blocked=true|false   whether any newer commit is held back (skipped > 0)
# and, when blocked, a markdown summary to $SCHEMA_REPORT (default: $RUNNER_TEMP or
# a temp dir, file schema-blocked.md).
set -euo pipefail

sub=external/EXDSchema
max=${MAX_CANDIDATES:-20}
out=${GITHUB_OUTPUT:-/dev/null}
tmp=${RUNNER_TEMP:-$(mktemp -d)}
report=${SCHEMA_REPORT:-$tmp/schema-blocked.md}
buildlog=$tmp/schema-build.log

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
  { echo "changed=false"; echo "skipped=0"; echo "blocked=false"; } >> "$out"
  exit 0
fi
echo "${#candidates[@]} new EXDSchema commit(s) since $(git -C "$sub" rev-parse --short "$pinned")."

short() { git -C "$sub" rev-parse --short "$1"; }
line() { git -C "$sub" log -1 --format='%h (%cs) %s' "$1"; }

# Markdown summary of the held-back commits (the first $1 candidates) and the build
# errors of the most recent failed attempt.
write_report() {
  local held=$1 now=$2
  {
    echo "EXDSchema \`latest\`의 최신 커밋이 현재 Lumina.Excel 생성기로 빌드되지 않아, 자동 갱신이 **${held}개 커밋을 보류** 중입니다."
    echo
    echo "- 현재 반영: \`$(line "$now")\`"
    echo "- upstream latest: \`$(line "${candidates[0]}")\`"
    echo
    echo "### 보류된 커밋"
    echo
    for ((k = 0; k < held; k++)); do echo "- \`$(line "${candidates[$k]}")\`"; done
    echo
    echo "### 빌드 오류 (가장 최근 실패한 시도)"
    echo
    echo '```'
    grep -E ': error [A-Z]+[0-9]+:' "$buildlog" | sed -E 's/^.*: error /error /; s/ \[[^]]*\]$//' | sort -u | head -10 || true
    echo '```'
    echo
    echo "업스트림에서 고쳐지면 다음 일일 실행에서 자동으로 반영되고 이 이슈는 닫힙니다. 오래 지속되면 업스트림 상태(EXDSchema PR, Lumina.Excel 생성기 지원 여부)를 확인하세요."
  } > "$report"
  echo "Wrote $report"
}

for i in "${!candidates[@]}"; do
  c=${candidates[$i]}
  git -C "$sub" checkout -q "$c"
  echo "::group::Build with EXDSchema $(line "$c")"
  if dotnet build SharpVision.csproj -c Release -nologo 2>&1 | tee "$buildlog.try"; then
    echo "::endgroup::"
    echo "Now at $(line "$c")"
    { echo "changed=true"; echo "skipped=$i"; } >> "$out"
    if [ "$i" -gt 0 ]; then
      echo "::warning::Newest EXDSchema commit $(short "${candidates[0]}") does not build; using $(short "$c") instead. $i newer commit(s) held back until fixed upstream."
      echo "blocked=true" >> "$out"
      write_report "$i" "$c"
    else
      echo "blocked=false" >> "$out"
    fi
    exit 0
  fi
  echo "::endgroup::"
  mv "$buildlog.try" "$buildlog"
  echo "EXDSchema $(line "$c") does not build; trying the previous commit."
done

# Nothing newer builds: stay put. Not a failure — the tracking issue reports it once
# instead of a failed run (and a mail) every day.
git -C "$sub" checkout -q "$pinned"
echo "::warning::None of the ${#candidates[@]} newer EXDSchema commits builds; staying at $(short "$pinned") until fixed upstream (or the Lumina.Excel generator is updated)."
{ echo "changed=false"; echo "skipped=${#candidates[@]}"; echo "blocked=true"; } >> "$out"
write_report "${#candidates[@]}" "$pinned"
exit 0
