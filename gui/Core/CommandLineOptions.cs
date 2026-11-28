using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace HNAUnlocker.Core
{
    public enum CommandLineAction { None, Launch, Attach }

    /// <summary>A rejected command line: a localization key plus the offending text.</summary>
    public sealed class CommandLineError
    {
        public string Key { get; }
        public string Value { get; }
        internal CommandLineError(string key, string value = null) { Key = key; Value = value ?? string.Empty; }
        public string Message => L10n.T(Key, Value);
    }

    /// <summary>
    /// Options accepted on the HNAUnlocker command line.  Parsing is all-or-nothing: any invalid
    /// option rejects the whole command line so a typo never applies half of a shortcut.
    /// Values only describe one run; the caller decides how they interact with saved settings.
    /// </summary>
    public sealed class CommandLineOptions
    {
        public const int FpsMin = 10, FpsMax = 480;
        public const double FovMin = -60, FovMax = 60;
        internal const int MaxArguments = 128;
        internal const int MaxGameArguments = 64;
        internal const int MaxGameArgumentCharacters = 4096;
        internal const int MaxCommandLineCharacters = 16384;

        public CommandLineAction Action { get; private set; }
        public int? Fps { get; private set; }
        public double? Fov { get; private set; }
        public bool? AntiFade { get; private set; }
        public bool? ExitWithGame { get; private set; }
        public string GamePath { get; private set; }
        public bool Minimized { get; private set; }
        public bool ShowHelp { get; private set; }
        public IReadOnlyList<string> GameArguments { get; private set; } = Array.Empty<string>();

        public bool IsEmpty => Action == CommandLineAction.None && Fps == null && Fov == null && AntiFade == null
            && ExitWithGame == null && GamePath == null && !Minimized && !ShowHelp && GameArguments.Count == 0;

        public CommandLineOptions() { }

        /// <summary>Options for a preset shortcut built from the current window values.</summary>
        public static CommandLineOptions ForShortcut(int fps, double fov, bool antiFade, string gamePath = null) =>
            new CommandLineOptions
            {
                Action = CommandLineAction.Launch,
                Fps = Math.Clamp(fps, FpsMin, FpsMax),
                Fov = Math.Round(Math.Clamp(fov, FovMin, FovMax), 2),
                AntiFade = antiFade,
                GamePath = string.IsNullOrWhiteSpace(gamePath) ? null : gamePath
            };

        public static bool TryParse(IReadOnlyList<string> args, out CommandLineOptions options, out CommandLineError error)
        {
            options = null;
            error = null;
            args ??= Array.Empty<string>();
            if (args.Count > MaxArguments || args.Sum(arg => (arg?.Length ?? 0) + 1) > MaxCommandLineCharacters)
            {
                error = new CommandLineError("Cli.TooLong");
                return false;
            }

            var result = new CommandLineOptions();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (arg.IndexOf('\0') >= 0) { error = new CommandLineError("Cli.InvalidCharacter"); return false; }
                if (arg == "--")
                {
                    var rest = args.Skip(i + 1).ToArray();
                    if (rest.Length > MaxGameArguments || rest.Sum(a => a.Length) > MaxGameArgumentCharacters)
                    { error = new CommandLineError("Cli.GameArgumentsTooLong"); return false; }
                    result.GameArguments = rest;
                    break;
                }
                if (arg is "-h" or "/?") arg = "--help";
                if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
                { error = new CommandLineError("Cli.UnknownOption", arg); return false; }

                string name = arg, inline = null;
                int equals = arg.IndexOf('=');
                if (equals > 2) { name = arg.Substring(0, equals); inline = arg.Substring(equals + 1); }
                name = name.ToLowerInvariant();
                bool takesValue = name is "--fps" or "--fov" or "--anti-fade" or "--exit-with-game" or "--game";
                bool known = takesValue || name is "--launch" or "--attach" or "--minimized" or "--help";
                if (!known) { error = new CommandLineError("Cli.UnknownOption", name); return false; }
                if (!seen.Add(name)) { error = new CommandLineError("Cli.DuplicateOption", name); return false; }
                if (!takesValue && inline != null) { error = new CommandLineError("Cli.UnexpectedValue", name); return false; }

                string value = inline;
                if (takesValue && value == null)
                {
                    if (i + 1 >= args.Count || args[i + 1] == "--")
                    { error = new CommandLineError("Cli.MissingValue", name); return false; }
                    value = args[++i] ?? string.Empty;
                    if (value.IndexOf('\0') >= 0) { error = new CommandLineError("Cli.InvalidCharacter"); return false; }
                }

                switch (name)
                {
                    case "--launch": result.Action = CommandLineAction.Launch; break;
                    case "--attach": result.Action = CommandLineAction.Attach; break;
                    case "--minimized": result.Minimized = true; break;
                    case "--help": result.ShowHelp = true; break;
                    case "--fps":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps)
                            || fps < FpsMin || fps > FpsMax)
                        { error = new CommandLineError("Cli.InvalidFps", value); return false; }
                        result.Fps = fps;
                        break;
                    case "--fov":
                        if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out double fov)
                            || !double.IsFinite(fov) || fov < FovMin || fov > FovMax)
                        { error = new CommandLineError("Cli.InvalidFov", value); return false; }
                        result.Fov = Math.Round(fov, 2);
                        break;
                    case "--anti-fade":
                        if (!TryParseSwitch(value, out bool antiFade))
                        { error = new CommandLineError("Cli.InvalidSwitch", name); return false; }
                        result.AntiFade = antiFade;
                        break;
                    case "--exit-with-game":
                        if (!TryParseSwitch(value, out bool exitWithGame))
                        { error = new CommandLineError("Cli.InvalidSwitch", name); return false; }
                        result.ExitWithGame = exitWithGame;
                        break;
                    case "--game":
                        if (!IsGamePathShape(value)) { error = new CommandLineError("Cli.InvalidGamePath", value); return false; }
                        result.GamePath = Path.GetFullPath(value);
                        break;
                }
            }

            if (seen.Contains("--launch") && seen.Contains("--attach"))
            { error = new CommandLineError("Cli.ConflictingActions"); return false; }
            if (result.Action == CommandLineAction.Attach && result.GameArguments.Count > 0)
            { error = new CommandLineError("Cli.GameArgumentsNeedLaunch"); return false; }
            options = result;
            return true;
        }

        private static bool TryParseSwitch(string value, out bool result)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "on": case "true": case "1": result = true; return true;
                case "off": case "false": case "0": result = false; return true;
                default: result = false; return false;
            }
        }

        /// <summary>Shape only; whether the file exists is checked when the options are applied.</summary>
        internal static bool IsGamePathShape(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            try
            {
                return Path.IsPathFullyQualified(path)
                    && string.Equals(Path.GetFileName(Path.GetFullPath(path)), "NexusAnima.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        }

        /// <summary>Canonical argument list; parsing it yields equal options.</summary>
        public IReadOnlyList<string> ToArguments()
        {
            var args = new List<string>();
            if (ShowHelp) args.Add("--help");
            if (Action == CommandLineAction.Launch) args.Add("--launch");
            if (Action == CommandLineAction.Attach) args.Add("--attach");
            if (GamePath != null) { args.Add("--game"); args.Add(GamePath); }
            if (Fps != null) { args.Add("--fps"); args.Add(Fps.Value.ToString(CultureInfo.InvariantCulture)); }
            if (Fov != null) { args.Add("--fov"); args.Add(Fov.Value.ToString("0.##", CultureInfo.InvariantCulture)); }
            if (AntiFade != null) { args.Add("--anti-fade"); args.Add(AntiFade.Value ? "on" : "off"); }
            if (ExitWithGame != null) { args.Add("--exit-with-game"); args.Add(ExitWithGame.Value ? "on" : "off"); }
            if (Minimized) args.Add("--minimized");
            if (GameArguments.Count > 0) { args.Add("--"); args.AddRange(GameArguments); }
            return args;
        }

        /// <summary>True when both describe the same run, ignoring argument order and spelling.</summary>
        public bool IsEquivalentTo(CommandLineOptions other) =>
            other != null
            && string.Equals(GamePath, other.GamePath, StringComparison.OrdinalIgnoreCase)
            && WithoutGame().ToArguments().SequenceEqual(other.WithoutGame().ToArguments(), StringComparer.Ordinal);

        private CommandLineOptions WithoutGame()
        {
            var copy = (CommandLineOptions)MemberwiseClone();
            copy.GamePath = null;
            return copy;
        }

        // ------------------------------------------------------------------ quoting
        /// <summary>Joins arguments so that CommandLineToArgvW returns them unchanged.</summary>
        public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

        internal static string Quote(string arg)
        {
            arg ??= string.Empty;
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder(arg.Length + 2).Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }
                if (i == arg.Length) { sb.Append('\\', backslashes * 2); break; }
                if (arg[i] == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
                else sb.Append('\\', backslashes).Append(arg[i]);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Splits an argument string (no program name) exactly as Windows does.</summary>
        public static IReadOnlyList<string> Split(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return Array.Empty<string>();
            // CommandLineToArgvW applies program-name rules to the first token; give it a dummy one.
            IntPtr argv = CommandLineToArgvW("x " + arguments, out int count);
            if (argv == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new string[Math.Max(0, count - 1)];
                for (int i = 1; i < count; i++)
                    result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return result;
            }
            finally { LocalFree(argv); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
