using HNAUnlocker.Core;

/// <summary>Writes and reads real .lnk files, but only inside an owned temporary directory.</summary>
internal static class ShortcutVerification
{
    public static void Run(Action<bool, string> check)
    {
        check(DesktopShortcuts.SanitizeName("HNAUnlocker 144 FPS") == "HNAUnlocker 144 FPS",
            "a normal shortcut name is kept");
        check(DesktopShortcuts.SanitizeName("a/b:c*?\"<>|d.") == "a b c d" && DesktopShortcuts.SanitizeName(" ... ") == "HNAUnlocker"
            && DesktopShortcuts.SanitizeName(null) == "HNAUnlocker" && DesktopShortcuts.SanitizeName(new string('x', 200)).Length == 80,
            "shortcut names drop characters Windows refuses and stay a usable length");

        string root = Path.Combine(Path.GetTempPath(), "HNAUnlocker-shortcut-" + Guid.NewGuid().ToString("N"));
        string other = Path.Combine(root, "other");
        Directory.CreateDirectory(other);
        try
        {
            string exe = Environment.ProcessPath;
            var preset = CommandLineOptions.ForShortcut(144, 5, true);

            ShortcutResult first = DesktopShortcuts.Ensure(root, new[] { other }, exe, preset, "HNAUnlocker 144 FPS", "fixture");
            check(first.Outcome == ShortcutOutcome.Created && File.Exists(first.Path)
                && Path.GetFileName(first.Path) == "HNAUnlocker 144 FPS.lnk",
                "the first request writes a named shortcut");

            ShortcutResult again = DesktopShortcuts.Ensure(root, new[] { other }, exe, CommandLineOptions.ForShortcut(144, 5, true),
                "HNAUnlocker 144 FPS", "fixture");
            check(again.Outcome == ShortcutOutcome.AlreadyExists && again.Path == first.Path
                && Directory.GetFiles(root, "*.lnk").Length == 1,
                "an equivalent shortcut is found instead of creating a duplicate");

            ShortcutResult changed = DesktopShortcuts.Ensure(root, new[] { other }, exe, CommandLineOptions.ForShortcut(144, 10, true),
                "HNAUnlocker 144 FPS", "fixture");
            check(changed.Outcome == ShortcutOutcome.Created && Path.GetFileName(changed.Path) == "HNAUnlocker 144 FPS (2).lnk",
                "different values under the same name get a numbered file instead of overwriting");

            // A shortcut elsewhere (the public desktop in the app) counts as existing too.
            ShortcutResult elsewhere = DesktopShortcuts.Ensure(other, Array.Empty<string>(), exe,
                CommandLineOptions.ForShortcut(240, 0, false), "HNAUnlocker 240 FPS", "fixture");
            ShortcutResult fromRoot = DesktopShortcuts.Ensure(root, new[] { other }, exe,
                CommandLineOptions.ForShortcut(240, 0, false), "HNAUnlocker 240 FPS", "fixture");
            check(elsewhere.Outcome == ShortcutOutcome.Created && fromRoot.Outcome == ShortcutOutcome.AlreadyExists
                && fromRoot.Path == elsewhere.Path,
                "a matching shortcut in another searched folder is reported instead of creating one");

            string foreignTarget = Path.Combine(root, "Other.exe");
            File.WriteAllBytes(foreignTarget, new byte[] { 0x4D, 0x5A });
            var foreign = DesktopShortcuts.Ensure(Path.Combine(root, "foreign"), Array.Empty<string>(), foreignTarget,
                CommandLineOptions.ForShortcut(60, 0, false), "HNAUnlocker 60 FPS", "fixture");
            File.Move(foreign.Path, Path.Combine(root, "Foreign.lnk"));
            File.WriteAllText(Path.Combine(root, "Broken.lnk"), "not a shortcut");
            ShortcutResult ours = DesktopShortcuts.Ensure(root, new[] { other }, exe,
                CommandLineOptions.ForShortcut(60, 0, false), "HNAUnlocker 60 FPS", "fixture");
            check(ours.Outcome == ShortcutOutcome.Created,
                "shortcuts to another program and unreadable .lnk files are never treated as a match");

            ShellLink.Info read = ShellLink.Read(first.Path);
            check(string.Equals(read.Target, exe, StringComparison.OrdinalIgnoreCase)
                && read.Arguments == "--launch --fps 144 --fov 5 --anti-fade on",
                "the written shortcut targets the app with the canonical preset arguments");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        check(!Directory.Exists(root), "shortcut fixtures remove their temporary directory");
    }
}
