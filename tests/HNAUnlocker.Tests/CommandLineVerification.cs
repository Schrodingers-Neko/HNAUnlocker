using HNAUnlocker.Core;

internal static class CommandLineVerification
{
    private const string Game = @"C:\Games\Nexus Anima\NexusAnima.exe";

    public static void Run(Action<bool, string> check)
    {
        VerifyParsing(check);
        VerifyRejections(check);
        VerifyRoundTrip(check);
        VerifyQuoting(check);
        VerifyLocalization(check);
    }

    private static CommandLineOptions Parse(params string[] args) =>
        CommandLineOptions.TryParse(args, out var options, out _) ? options : null;

    private static string ErrorKey(params string[] args) =>
        CommandLineOptions.TryParse(args, out _, out var error) ? null : error.Key;

    private static void VerifyParsing(Action<bool, string> check)
    {
        check(Parse()?.IsEmpty == true, "an empty command line parses to no options");

        var full = Parse("--launch", "--fps", "144", "--fov", "-3.5", "--anti-fade", "on", "--exit-with-game", "off",
            "--minimized", "--game", Game, "--", "-screen-fullscreen", "0", "--fps");
        check(full != null && full.Action == CommandLineAction.Launch && full.Fps == 144 && full.Fov == -3.5
            && full.AntiFade == true && full.ExitWithGame == false && full.Minimized
            && string.Equals(full.GamePath, Game, StringComparison.OrdinalIgnoreCase)
            && full.GameArguments.SequenceEqual(new[] { "-screen-fullscreen", "0", "--fps" }),
            "every option parses, and everything after -- is kept verbatim as game arguments");

        var inline = Parse("--FPS=60", "--anti-fade=false", "--attach");
        check(inline != null && inline.Fps == 60 && inline.AntiFade == false && inline.Action == CommandLineAction.Attach,
            "option names are case-insensitive and accept --name=value");

        check(Parse("--fov", "1.239")?.Fov == 1.24, "FOV values round to two decimals like the window");
        check(Parse("--fps", "10")?.Fps == 10 && Parse("--fps", "480")?.Fps == 480
            && Parse("--fov", "-60")?.Fov == -60 && Parse("--fov", "60")?.Fov == 60,
            "range limits are inclusive");
        check(Parse("-h")?.ShowHelp == true && Parse("/?")?.ShowHelp == true && Parse("--help")?.ShowHelp == true,
            "help has the usual spellings");
        check(Parse("--anti-fade", "1")?.AntiFade == true && Parse("--anti-fade", "OFF")?.AntiFade == false,
            "switches accept on/off, true/false and 1/0");
    }

    private static void VerifyRejections(Action<bool, string> check)
    {
        foreach (var sample in new (string[] Args, string Key, string Name)[]
        {
            (new[] { "--speed", "2" }, "Cli.UnknownOption", "an unknown option"),
            (new[] { "launch" }, "Cli.UnknownOption", "a positional word"),
            (new[] { "--" + "" + "-launch" }, "Cli.UnknownOption", "a triple-dash option"),
            (new[] { "--fps", "60", "--fps", "90" }, "Cli.DuplicateOption", "a repeated option"),
            (new[] { "--fps" }, "Cli.MissingValue", "a missing value at the end"),
            (new[] { "--fps", "--", "x" }, "Cli.MissingValue", "a value that is really the -- separator"),
            (new[] { "--fps", "9" }, "Cli.InvalidFps", "an FPS below 10"),
            (new[] { "--fps", "481" }, "Cli.InvalidFps", "an FPS above 480"),
            (new[] { "--fps", "1.5" }, "Cli.InvalidFps", "a fractional FPS"),
            (new[] { "--fps", "abc" }, "Cli.InvalidFps", "a non-numeric FPS"),
            (new[] { "--fov", "61" }, "Cli.InvalidFov", "an FOV above 60"),
            (new[] { "--fov", "NaN" }, "Cli.InvalidFov", "a NaN FOV"),
            (new[] { "--fov", "1e1" }, "Cli.InvalidFov", "an exponent FOV"),
            (new[] { "--fov", "1,5" }, "Cli.InvalidFov", "a culture-specific decimal comma"),
            (new[] { "--anti-fade", "maybe" }, "Cli.InvalidSwitch", "an unknown switch value"),
            (new[] { "--launch=yes" }, "Cli.UnexpectedValue", "a value on a flag"),
            (new[] { "--launch", "--attach" }, "Cli.ConflictingActions", "both actions"),
            (new[] { "--attach", "--", "-x" }, "Cli.GameArgumentsNeedLaunch", "game arguments with --attach"),
            (new[] { "--game", @"Games\NexusAnima.exe" }, "Cli.InvalidGamePath", "a relative game path"),
            (new[] { "--game", @"C:\Games\Other.exe" }, "Cli.InvalidGamePath", "a different executable name"),
            (new[] { "--game", "" }, "Cli.InvalidGamePath", "an empty game path"),
            (new[] { "--fps", "6\u00000" }, "Cli.InvalidCharacter", "an embedded NUL"),
            (Enumerable.Repeat("--minimized", 129).ToArray(), "Cli.TooLong", "too many arguments"),
            (new[] { "--launch", "--" }.Concat(Enumerable.Repeat("x", 65)).ToArray(), "Cli.GameArgumentsTooLong", "too many game arguments"),
            (new[] { "--launch", "--", new string('x', 4097) }, "Cli.GameArgumentsTooLong", "overlong game arguments"),
        })
            check(ErrorKey(sample.Args) == sample.Key, $"command line rejects {sample.Name} as a whole");
    }

    private static void VerifyRoundTrip(Action<bool, string> check)
    {
        foreach (var args in new[]
        {
            new[] { "--launch", "--fps", "144", "--fov", "5", "--anti-fade", "on" },
            new[] { "--attach", "--minimized", "--exit-with-game", "on" },
            new[] { "--game", Game, "--fov", "-0.25", "--launch", "--", "-popupwindow", "a b" },
            new[] { "--help" },
        })
        {
            var original = Parse(args);
            var again = Parse(original.ToArguments().ToArray());
            var split = Parse(CommandLineOptions.Split(CommandLineOptions.Join(original.ToArguments())).ToArray());
            check(original.IsEquivalentTo(again) && original.IsEquivalentTo(split)
                && again.ToArguments().SequenceEqual(original.ToArguments()),
                "canonical arguments parse back to the same options: " + string.Join(" ", args));
        }

        check(Parse("--fps", "60", "--launch").IsEquivalentTo(Parse("--launch", "--fps=60")),
            "equivalence ignores option order and spelling");
        check(Parse("--launch", "--game", Game).IsEquivalentTo(Parse("--launch", "--game", Game.ToUpperInvariant())),
            "equivalence compares game paths without case");
        check(!Parse("--launch", "--fps", "60").IsEquivalentTo(Parse("--launch", "--fps", "61"))
            && !Parse("--launch").IsEquivalentTo(Parse("--attach"))
            && !Parse("--launch").IsEquivalentTo(Parse("--launch", "--game", Game)),
            "different values, actions or game paths are not equivalent");

        var preset = CommandLineOptions.ForShortcut(999, -75.555, true);
        check(preset.Action == CommandLineAction.Launch && preset.Fps == 480 && preset.Fov == -60
            && preset.AntiFade == true && preset.GamePath == null && preset.ExitWithGame == null && !preset.Minimized,
            "shortcut presets clamp values and only describe launch, frame rate, FOV and anti-fade");
    }

    private static void VerifyQuoting(Action<bool, string> check)
    {
        string[] samples =
        {
            "plain", "two words", "", "\"quoted\"", "x\\\"y", @"C:\path with space\", @"\\server\share\",
            "trailing\\\\", "tab\there", "a\\b", "ends with quote\"", "--fps", "日本語 パス"
        };
        string joined = CommandLineOptions.Join(samples);
        check(CommandLineOptions.Split(joined).SequenceEqual(samples),
            "quoting round-trips through the Windows command-line splitter");
        check(CommandLineOptions.Split("").Count == 0 && CommandLineOptions.Split("   ").Count == 0,
            "an empty argument string splits to nothing");
        check(CommandLineOptions.Join(new[] { "--fps", "144" }) == "--fps 144",
            "simple arguments are left unquoted");
    }

    private static void VerifyLocalization(Action<bool, string> check)
    {
        string original = L10n.Preference;
        try
        {
            string[] keys =
            {
                "Cli.TooLong", "Cli.InvalidCharacter", "Cli.GameArgumentsTooLong", "Cli.UnknownOption",
                "Cli.DuplicateOption", "Cli.UnexpectedValue", "Cli.MissingValue", "Cli.InvalidFps", "Cli.InvalidFov",
                "Cli.InvalidSwitch", "Cli.InvalidGamePath", "Cli.ConflictingActions", "Cli.GameArgumentsNeedLaunch",
                "Cli.GameNotFound", "Cli.HelpText", "Cli.HelpTitle", "Cli.ErrorTitle", "Cli.SeeHelp",
                "Cli.CreateShortcut", "Cli.CreateShortcutTip", "Cli.ShortcutCreated", "Cli.ShortcutExists",
                "Cli.ShortcutFailed", "Runtime.Session.GameArgumentsIgnoredRunning"
            };
            foreach (string language in new[] { "zh-CN", "en-US", "ja-JP", "ko-KR" })
            {
                L10n.SetLanguage(language);
                check(keys.All(key => L10n.T(key, "x", "y") is string text && text != key && text.Length > 0),
                    $"command-line messages resolve in {language}");
                check(L10n.T("Cli.InvalidFps", "999").Contains("999") && L10n.T("Cli.ShortcutName", 144).Contains("144")
                    && L10n.T("Cli.HelpText").Contains("--fps"),
                    $"command-line messages keep their values and option names in {language}");
            }
        }
        finally { L10n.SetLanguage(original); }
    }
}
