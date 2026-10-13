using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SharpVision.Cli;

/// Auto-detects installed FFXIV clients (global/Steam/Korean/Chinese/Taiwan) and
/// resolves each to its sqpack folder, for the interactive picker. The region and
/// languages are read from each client's real data later (see InteractiveMenu), so
/// only the sqpack paths are returned here. See the memory note "FFXIV client
/// install paths" for the rationale behind each source.
public static class GamePathFinder
{
    /// Distinct sqpack folder paths of detected installs.
    public static List<string> Find()
    {
        var found = new List<string>();

        void Add(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return;
            var sqpack = Path.Combine(installRoot, "game", "sqpack");
            if (Directory.Exists(sqpack) && !found.Any(f => PathEquals(f, sqpack)))
                found.Add(sqpack);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in RegistryInstalls())
                Add(root);
            foreach (var root in SteamCommonDirs())
                Add(root);
        }

        foreach (var root in DefaultRoots())
            Add(root);

        return found;
    }

    // The game keeps no dedicated install-path key, so we scan the Windows uninstall
    // entries for any "FINAL FANTASY XIV" install and take its InstallLocation.
    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryInstalls()
    {
        var locations = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, path) in locations)
        {
            using var key = hive.OpenSubKey(path);
            if (key == null)
                continue;

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub?.GetValue("DisplayName") is not string name
                    || sub.GetValue("InstallLocation") is not string loc
                    || loc.Length == 0
                    || !name.Contains("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return loc;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> SteamCommonDirs()
    {
        string? steam = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("SteamPath") as string
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string;
        if (string.IsNullOrEmpty(steam))
            yield break;

        var libraries = new List<string> { steam };

        // libraryfolders.vdf lists additional Steam library drives.
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (var lib in libraries)
        {
            yield return Path.Combine(lib, "steamapps", "common", "FINAL FANTASY XIV Online");
            yield return Path.Combine(lib, "steamapps", "common", "FINAL FANTASY XIV - A Realm Reborn");
        }
    }

    private static IEnumerable<string> DefaultRoots()
    {
        // Roots to scan for standard install subpaths: both Program Files (most
        // clients use (x86), but the Taiwan client uses 64-bit), plus each fixed
        // drive's root since the game is often installed outside Program Files
        // (e.g. D:\SquareEnix\..., D:\FINAL FANTASY XIV - KOREA).
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (OperatingSystem.IsWindows())
            foreach (var drive in DriveInfo.GetDrives())
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    roots.Add(drive.RootDirectory.FullName);

        string[][] windowsInstalls =
        {
            new[] { "SquareEnix", "FINAL FANTASY XIV - A Realm Reborn" },             // Global
            new[] { "Steam", "steamapps", "common", "FINAL FANTASY XIV Online" },     // Global (Steam)
            new[] { "Steam", "steamapps", "common", "FINAL FANTASY XIV - A Realm Reborn" },
            new[] { "FINAL FANTASY XIV - KOREA" },                                    // Korean
            new[] { "SNDA", "FFXIV" },                                                // Simplified Chinese
            new[] { "USERJOY GAMES", "FINAL FANTASY XIV TC" },                        // Traditional Chinese (Taiwan)
        };
        foreach (var root in roots.Where(r => r.Length > 0).Distinct())
            foreach (var parts in windowsInstalls)
                yield return Path.Combine(new[] { root }.Concat(parts).ToArray());

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            yield return Path.Combine(home, "Library", "Application Support", "FINAL FANTASY XIV");                  // macOS
            yield return Path.Combine(home, ".steam", "steam", "steamapps", "common", "FINAL FANTASY XIV Online");  // Linux (Steam)
            yield return Path.Combine(home, ".xlcore", "ffxiv");                                                    // Linux (XLCore)
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
