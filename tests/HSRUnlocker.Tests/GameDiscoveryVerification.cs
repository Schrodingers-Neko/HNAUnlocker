using Microsoft.Win32;
using StarRailUnlocker.Core;

internal static class GameDiscoveryVerification
{
    public static void Run(Action<bool, string> check)
    {
        // Each fixture replaces only OS reads; discovery and path/metadata validation stay real.
        var hyp = new Fixture();
        hyp.Hyp("miHoYo", "1.0", "abc_cn", @"D:\arbitrary folder\CN");
        hyp.Hyp("Cognosphere", "2.0", "abc_global", @"E:\自定义\游戏");
        hyp.Files.UnionWith(new[] { @"D:\arbitrary folder\CN\NexusAnima.exe", @"E:\自定义\游戏\NexusAnima.exe" });
        check(hyp.Find().SequenceEqual(new[] { @"D:\arbitrary folder\CN\NexusAnima.exe", @"E:\自定义\游戏\NexusAnima.exe" }),
            "HYP both vendors and businesses find existing executables in arbitrary and Unicode directories");

        var cross = new Fixture();
        cross.Hyp("miHoYo", "3", "abc_global", @"D:\global");
        cross.Hyp("Cognosphere", "4", "abc_cn", @"D:\china");
        cross.Files.UnionWith(new[] { @"D:\global\NexusAnima.exe", @"D:\china\NexusAnima.exe" });
        check(cross.Find().SequenceEqual(new[] { @"D:\china\NexusAnima.exe", @"D:\global\NexusAnima.exe" }),
            "HYP business identity is independent of vendor and directory spelling");

        var stale = new Fixture();
        stale.Hyp("miHoYo", "1", "abc_cn", @"D:\missing");
        stale.Hyp("miHoYo", "1", "hk4e_cn", @"D:\other");
        stale.Files.Add(@"D:\other\NexusAnima.exe");
        check(stale.Find().Count == 0, "stale HYP paths and other game businesses are ignored");

        var duplicates = new Fixture();
        duplicates.Hyp("miHoYo", "1", "abc_cn", @"D:\Game\.");
        duplicates.Hyp("Cognosphere", "2", "abc_global", @"d:\game");
        duplicates.Files.Add(@"D:\Game\NexusAnima.exe");
        check(duplicates.Find().Count == 1, "HYP paths normalize and deduplicate without case sensitivity");

        foreach (string invalid in new[] { @"\\server\game", @"\\?\D:\game", "https://example/game", @"D:game",
                     @"\game", @"game", @"D:\game:stream", @"D:\bad|name", "D:\\" + new string('x', 32768) })
        {
            var fixture = new Fixture();
            fixture.Hyp("miHoYo", "1", "abc_cn", invalid);
            fixture.ExistsByDefault = true;
            check(fixture.Find().Count == 0, "discovery rejects nonlocal, malformed or oversized path: " +
                (invalid.Length < 80 ? invalid : "oversized"));
        }

        var uninstall = new Fixture();
        uninstall.Uninstall(RegistryHive.CurrentUser, RegistryView.Registry64, "one", "Honkai: Nexus Anima", @"D:\custom");
        uninstall.Uninstall(RegistryHive.LocalMachine, RegistryView.Registry32, "two", "崩坏：因缘精灵", @"E:\中文");
        uninstall.Uninstall(RegistryHive.LocalMachine, RegistryView.Registry64, "three", "崩壞：因緣精靈", @"F:\繁體");
        uninstall.Uninstall(RegistryHive.CurrentUser, RegistryView.Registry32, "four", "Nexus Anima", @"G:\short");
        uninstall.Files.UnionWith(new[] { @"D:\custom\NexusAnima.exe", @"E:\中文\NexusAnima.exe", @"F:\繁體\NexusAnima.exe", @"G:\short\NexusAnima.exe" });
        check(uninstall.Find().Count == 4, "recognized uninstall names discover HKCU and both HKLM views");

        var unrelated = new Fixture();
        unrelated.Uninstall(RegistryHive.CurrentUser, RegistryView.Registry64, "bad", "Nexus Anima wallpaper", @"D:\fake");
        unrelated.Files.Add(@"D:\fake\NexusAnima.exe");
        check(unrelated.Find().Count == 0, "unrelated uninstall display names cannot nominate executables");

        var legacy = new Fixture();
        legacy.Uninstall(RegistryHive.LocalMachine, RegistryView.Registry32, "legacy", "Honkai Nexus Anima", @"D:\launcher");
        legacy.Lines[@"D:\launcher\config.ini"] = new[] { "[General]", @"game_install_path=D:\wrong", "[launcher]", @"game_install_path = E:/custom/game" };
        legacy.Files.UnionWith(new[] { @"D:\wrong\NexusAnima.exe", @"E:\custom\game\NexusAnima.exe" });
        check(legacy.Find().SequenceEqual(new[] { @"E:\custom\game\NexusAnima.exe" }),
            "legacy launcher config reads game_install_path only inside launcher section");
        legacy.Lines[@"D:\launcher\config.ini"] = new[] { "[launcher]", @"game_install_path=E:\custom\game", @"game_install_path=E:\conflict" };
        check(legacy.Find().Count == 0, "conflicting launcher paths are refused");
        legacy.Lines[@"D:\launcher\config.ini"] = new[] { "[launcher]", "game_install_path=" + new string('x', 65536) };
        check(legacy.Find().Count == 0, "oversized launcher metadata is refused before path probing");

        var access = new Fixture();
        access.Hyp("miHoYo", "bad", "abc_cn", @"D:\denied");
        access.Hyp("miHoYo", "file-denied", "abc_cn", @"D:\file-denied");
        access.Hyp("Cognosphere", "good", "abc_global", @"E:\working");
        access.Files.Add(@"E:\working\NexusAnima.exe");
        access.DeniedFile = @"D:\file-denied\NexusAnima.exe";
        access.DeniedRegistry = @"Software\miHoYo\HYP\bad";
        access.Uninstall(RegistryHive.CurrentUser, RegistryView.Registry64, "unreadable", "Nexus Anima", @"D:\unreadable");
        access.Lines[@"D:\unreadable\config.ini"] = null;
        check(access.Find().SequenceEqual(new[] { @"E:\working\NexusAnima.exe" }),
            "individual registry, file and metadata access errors do not hide other installations");

        var bounded = new Fixture();
        string hypRoot = @"Software\miHoYo\HYP";
        bounded.Children[Fixture.Key(RegistryHive.CurrentUser, RegistryView.Registry64, hypRoot)] =
            Enumerable.Range(0, 10000).Select(index => index.ToString()).ToArray();
        bounded.Hyp("miHoYo", "9999", "abc_cn", @"D:\late");
        bounded.Files.Add(@"D:\late\NexusAnima.exe");
        bounded.Children[Fixture.Key(RegistryHive.LocalMachine, RegistryView.Registry64, Fixture.UninstallRoot)] =
            Enumerable.Range(0, 10000).Select(index => index.ToString()).ToArray();
        bounded.Uninstall(RegistryHive.LocalMachine, RegistryView.Registry64, "9999", "Nexus Anima", @"D:\late");
        check(bounded.Find().Count == 0 && bounded.RegistryReads < 3000,
            "registry enumeration and candidate reads are bounded for oversized roots");
    }

    private sealed class Fixture
    {
        internal const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        internal readonly Dictionary<string, string[]> Children = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, string[]> Lines = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        internal bool ExistsByDefault;
        internal string DeniedRegistry, DeniedFile;
        internal int RegistryReads;

        internal static string Key(RegistryHive hive, RegistryView view, string path) => $"{hive}|{view}|{path}";

        internal void Hyp(string vendor, string version, string business, string directory)
        {
            string root = @"Software\" + vendor + @"\HYP";
            AddChild(RegistryHive.CurrentUser, RegistryView.Registry64, root, version);
            Values[Key(RegistryHive.CurrentUser, RegistryView.Registry64, root + "\\" + version + "\\" + business) + "|GameInstallPath"] = directory;
        }

        internal void Uninstall(RegistryHive hive, RegistryView view, string entry, string name, string directory)
        {
            AddChild(hive, view, UninstallRoot, entry);
            string key = Key(hive, view, UninstallRoot + "\\" + entry);
            Values[key + "|DisplayName"] = name;
            Values[key + "|InstallLocation"] = directory;
        }

        private void AddChild(RegistryHive hive, RegistryView view, string path, string name)
        {
            string key = Key(hive, view, path);
            if (!Children.TryGetValue(key, out var children)) children = Array.Empty<string>();
            if (!children.Contains(name)) Children[key] = children.Append(name).ToArray();
        }

        internal IReadOnlyList<string> Find() => GameInstallationDiscovery.FindInstalled(
            (hive, view, path) => Children.GetValueOrDefault(Key(hive, view, path)) ?? Array.Empty<string>(),
            (hive, view, path, name) =>
            {
                RegistryReads++;
                if (DeniedRegistry != null && path.StartsWith(DeniedRegistry, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException();
                return Values.GetValueOrDefault(Key(hive, view, path) + "|" + name);
            },
            path => path == DeniedFile ? throw new IOException() : (ExistsByDefault && !path.Equals(@"C:\Program Files\Nexus Anima\games\NexusAnima Game\NexusAnima.exe", StringComparison.OrdinalIgnoreCase)) || Files.Contains(path),
            path => Lines.TryGetValue(path, out var lines) && lines == null ? throw new IOException() : lines);
    }
}
