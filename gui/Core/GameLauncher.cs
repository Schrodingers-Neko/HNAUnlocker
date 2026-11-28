using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HNAUnlocker.Core
{
    /// <summary>Launch options for Honkai: Nexus Anima, including experimental mobile parameters.</summary>
    public static class GameLauncher
    {
        public static IReadOnlyList<string> MobileArguments(string executable, MobileGraphicsSource source, Action<string> log = null) =>
            MobileLaunchSettings.BuildArguments(executable, source, log);

        public static uint Launch(UnlockConfig config, Action<string> log = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(config.ProcessPath) || !File.Exists(config.ProcessPath)
                || !Path.GetFileName(config.ProcessPath).Equals("NexusAnima.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L10n.T("Runtime.Launcher.InvalidPath"));
            string compatibility = Environment.GetEnvironmentVariable("__COMPAT_LAYER") ?? "";
            if (compatibility.Contains("RunAsInvoker", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L10n.T("Runtime.Launcher.RunAsInvoker"));
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException(L10n.T("Runtime.Launcher.AdminRequired"));
            string[] extra = config.GameArguments ?? Array.Empty<string>();
            if (config.UseMobileUI)
            {
                // Mobile launch builds and verifies its own argument list; extra arguments are not mixed in.
                if (extra.Length > 0) log?.Invoke(L10n.T("Runtime.Launcher.GameArgumentsIgnoredMobile"));
                return MobileGameLauncher.Launch(config.ProcessPath,
                    MobileArguments(config.ProcessPath, config.GraphicsSource, log), log, cancellationToken,
                    config.SafeAreaMode, config.SafeAreaHorizontalPercent);
            }
            var start = new ProcessStartInfo(config.ProcessPath)
            {
                WorkingDirectory = Path.GetDirectoryName(config.ProcessPath),
                UseShellExecute = false,
            };
            foreach (string argument in extra) start.ArgumentList.Add(argument);
            if (extra.Length > 0) log?.Invoke(L10n.T("Runtime.Launcher.GameArguments", CommandLineOptions.Join(extra)));
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(start) ?? throw new InvalidOperationException(L10n.T("Runtime.Launcher.StartFailed"));
            return checked((uint)process.Id);
        }
    }
}
