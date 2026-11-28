using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HNAUnlocker.Core
{
    public enum ShortcutOutcome { Created, AlreadyExists }

    public sealed class ShortcutResult
    {
        public ShortcutOutcome Outcome { get; }
        public string Path { get; }
        internal ShortcutResult(ShortcutOutcome outcome, string path) { Outcome = outcome; Path = path; }
    }

    /// <summary>
    /// Creates preset shortcuts that start HNAUnlocker with command-line options, unless a
    /// shortcut to the same executable with equivalent options is already present.
    /// </summary>
    public static class DesktopShortcuts
    {
        private const int MaxNameLength = 80;

        /// <summary>
        /// Looks in every <paramref name="searchDirectories"/> for an equivalent shortcut and
        /// otherwise writes a new one into <paramref name="createDirectory"/>.
        /// </summary>
        public static ShortcutResult Ensure(string createDirectory, IEnumerable<string> searchDirectories,
            string executable, CommandLineOptions options, string name, string description)
        {
            if (string.IsNullOrWhiteSpace(createDirectory)) throw new ArgumentException("A shortcut folder is required.", nameof(createDirectory));
            if (options == null) throw new ArgumentNullException(nameof(options));
            executable = Path.GetFullPath(executable);

            string existing = FindEquivalent(searchDirectories.Prepend(createDirectory), executable, options);
            if (existing != null) return new ShortcutResult(ShortcutOutcome.AlreadyExists, existing);

            Directory.CreateDirectory(createDirectory);
            string path = UniquePath(createDirectory, SanitizeName(name));
            ShellLink.Write(path, executable, CommandLineOptions.Join(options.ToArguments()),
                Path.GetDirectoryName(executable), description);
            return new ShortcutResult(ShortcutOutcome.Created, path);
        }

        internal static string FindEquivalent(IEnumerable<string> directories, string executable, CommandLineOptions options)
        {
            foreach (string directory in directories.Where(d => !string.IsNullOrWhiteSpace(d))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> links;
                try { links = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.lnk").ToArray() : Array.Empty<string>(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (string link in links)
                {
                    try
                    {
                        ShellLink.Info info = ShellLink.Read(link);
                        if (string.IsNullOrEmpty(info.Target)
                            || !string.Equals(Path.GetFullPath(info.Target), executable, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (CommandLineOptions.TryParse(CommandLineOptions.Split(info.Arguments), out var found, out _)
                            && found.IsEquivalentTo(options))
                            return link;
                    }
                    catch (Exception) { /* An unreadable or foreign shortcut is never a match. */ }
                }
            }
            return null;
        }

        internal static string SanitizeName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var sb = new StringBuilder();
            foreach (char c in name ?? string.Empty) sb.Append(invalid.Contains(c) || char.IsControl(c) ? ' ' : c);
            string result = string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
            if (result.Length > MaxNameLength) result = result.Substring(0, MaxNameLength).TrimEnd(' ', '.');
            return result.Length == 0 ? "HNAUnlocker" : result;
        }

        internal static string UniquePath(string directory, string name)
        {
            string path = Path.Combine(directory, name + ".lnk");
            for (int i = 2; File.Exists(path) || Directory.Exists(path); i++)
            {
                if (i > 99) throw new IOException("Too many shortcuts share this name.");
                path = Path.Combine(directory, $"{name} ({i}).lnk");
            }
            return path;
        }
    }
}
