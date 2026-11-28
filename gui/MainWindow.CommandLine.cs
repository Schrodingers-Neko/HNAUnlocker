using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HNAUnlocker.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HNAUnlocker
{
    /// <summary>
    /// Command-line options, run-only values, and preset desktop shortcuts.  Values from the
    /// command line apply to this run only: saving writes the earlier values for those fields
    /// unless the user changes the same field in the window.
    /// </summary>
    public sealed partial class MainWindow
    {
        private const string RunOnlyFps = "fps", RunOnlyFov = "fov", RunOnlyAntiFade = "anti-fade",
            RunOnlyExitWithGame = "exit-with-game", RunOnlyGame = "game";

        private readonly HashSet<string> _runOnly = new(StringComparer.Ordinal);
        private int _savedFps;
        private double _savedFov;
        private bool _savedAntiFade, _savedExitWithGame;
        private string _savedProcessPath, _runOnlyGamePath;
        private string[] _savedGamePaths = Array.Empty<string>();
        private CommandLineOptions _startupOptions;
        private CommandLineError _startupError;
        private bool _applyingCommandLine;

        // ------------------------------------------------------------------ saving
        /// <summary>Saves settings, keeping the saved value of every field the command line replaced.</summary>
        private bool SaveConfig(bool logFailure = true)
        {
            UnlockConfig toSave = _config;
            if (_runOnly.Count > 0)
            {
                toSave = _config.Clone();
                if (_runOnly.Contains(RunOnlyFps)) toSave.TargetFramerate = _savedFps;
                if (_runOnly.Contains(RunOnlyFov)) toSave.FovDelta = _savedFov;
                if (_runOnly.Contains(RunOnlyAntiFade)) toSave.DisableCameraFade = _savedAntiFade;
                if (_runOnly.Contains(RunOnlyExitWithGame)) toSave.ExitWithGame = _savedExitWithGame;
                if (_runOnly.Contains(RunOnlyGame))
                {
                    toSave.ProcessPath = _savedProcessPath;
                    // Keep clients found during this run, but not the one named only on the command line.
                    toSave.GamePaths = (_config.GamePaths ?? Array.Empty<string>())
                        .Where(path => !string.Equals(path, _runOnlyGamePath, StringComparison.OrdinalIgnoreCase)
                            || _savedGamePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                }
            }
            bool saved = toSave.Save();
            if (!saved && logFailure && !_closed) Log(L10n.T("Ui.LogConfigSaveFailed", UnlockConfig.LogPath));
            return saved;
        }

        private void MarkRunOnly(string field)
        {
            if (!_runOnly.Add(field)) return;
            switch (field)
            {
                case RunOnlyFps: _savedFps = _config.TargetFramerate; break;
                case RunOnlyFov: _savedFov = _config.FovDelta; break;
                case RunOnlyAntiFade: _savedAntiFade = _config.DisableCameraFade; break;
                case RunOnlyExitWithGame: _savedExitWithGame = _config.ExitWithGame; break;
                case RunOnlyGame:
                    _savedProcessPath = _config.ProcessPath;
                    _savedGamePaths = (_config.GamePaths ?? Array.Empty<string>()).ToArray();
                    break;
            }
        }

        /// <summary>A change made in the window replaces the run-only value and is saved normally.</summary>
        private void ClearRunOnly(string field)
        {
            if (!_applyingCommandLine) _runOnly.Remove(field);
        }

        // ------------------------------------------------------------------ applying options
        private void ReadStartupCommandLine(string[] args)
        {
            if (args == null || args.Length == 0) return;
            if (CommandLineOptions.TryParse(args, out var options, out var error)) _startupOptions = options;
            else _startupError = error;
        }

        private void ApplyCommandLineValues(CommandLineOptions options, bool startup)
        {
            if (options == null || options.ShowHelp) return;
            _applyingCommandLine = true;
            try
            {
                if (options.GamePath != null)
                {
                    if (IsValidGamePath(options.GamePath))
                    {
                        MarkRunOnly(RunOnlyGame);
                        _runOnlyGamePath = GameClientPaths.Normalize(new[] { options.GamePath }).FirstOrDefault();
                        _config.ProcessPath = _runOnlyGamePath;
                        _localErrorKey = null;
                        RefreshGameClients();
                        Log(L10n.T("Cli.LogGamePath", _runOnlyGamePath));
                    }
                    else ShowCommandLineError(new CommandLineError("Cli.GameNotFound", options.GamePath));
                }
                if (options.Fps is int fps)
                {
                    MarkRunOnly(RunOnlyFps);
                    ApplyFps(fps);
                }
                if (options.Fov is double fov)
                {
                    MarkRunOnly(RunOnlyFov);
                    if (startup)
                    {
                        _config.FovDelta = fov;
                        _syncingFov = true;
                        try { FovSlider.Value = FovBox.Value = fov; }
                        finally { _syncingFov = false; }
                    }
                    else ApplyFovValue(fov);
                }
                if (options.AntiFade is bool antiFade)
                {
                    MarkRunOnly(RunOnlyAntiFade);
                    _config.DisableCameraFade = antiFade;
                    AntiFadeToggle.IsOn = antiFade;
                }
                if (options.ExitWithGame is bool exitWithGame)
                {
                    MarkRunOnly(RunOnlyExitWithGame);
                    _config.ExitWithGame = exitWithGame;
                    ExitWithGameToggle.IsOn = exitWithGame;
                }
                if (options.GameArguments.Count > 0)
                    _config.GameArguments = options.GameArguments.ToArray();
            }
            finally { _applyingCommandLine = false; }
        }

        private async Task RunCommandLineActionAsync(CommandLineAction action)
        {
            if (_closed || _closePending || action == CommandLineAction.None) return;
            if (_session?.IsRunning == true || !CanChangeGameClient)
            {
                Log(L10n.T("Cli.LogActionIgnored"));
                return;
            }
            if (action == CommandLineAction.Attach)
            {
                await AttachToRunningGameAsync();
                return;
            }
            if (!IsValidGamePath(_config.ProcessPath))
            {
                Log(L10n.T("Ui.LogInvalidGamePath"));
                return;
            }
            Log(L10n.T("Ui.LogStartingGame"));
            LaunchSession(0, launchIfMissing: true);
        }

        /// <summary>Called on the tray window's thread when a second copy forwards its arguments.</summary>
        private void OnForwardedCommandLine(string[] args) =>
            DispatcherQueue.TryEnqueue(() => _ = HandleForwardedCommandLineAsync(args));

        private async Task HandleForwardedCommandLineAsync(string[] args)
        {
            if (_closed || _closePending) return;
            if (!CommandLineOptions.TryParse(args, out var options, out var error))
            {
                ShowCommandLineError(error);
                return;
            }
            Log(L10n.T("Cli.LogForwarded", CommandLineOptions.Join(args)));
            if (options.ShowHelp)
            {
                await ShowCommandLineHelpAsync();
                return;
            }
            ApplyCommandLineValues(options, startup: false);
            RefreshPresentation();
            if (options.Action != CommandLineAction.None)
            {
                if (!_startupComplete) Log(L10n.T("Cli.LogActionIgnored"));
                else await RunCommandLineActionAsync(options.Action);
            }
            if (options.Minimized) HideToTray(force: true);
        }

        // ------------------------------------------------------------------ feedback
        private void ShowCommandLineError(CommandLineError error)
        {
            if (error == null || _closed) return;
            Log(L10n.T("Cli.LogRejected", error.Message));
            CommandLineInfoBar.Title = L10n.T("Cli.ErrorTitle");
            CommandLineInfoBar.Message = error.Message + " " + L10n.T("Cli.SeeHelp");
            CommandLineInfoBar.IsOpen = true;
        }

        private async Task ShowCommandLineHelpAsync()
        {
            if (_closed || _closePending || _dialogOpen || _dialogRoot == null) return;
            _dialogOpen = true;
            try
            {
                var text = new TextBlock
                {
                    Text = L10n.T("Cli.HelpText"),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                };
                var dialog = new ContentDialog
                {
                    XamlRoot = _dialogRoot,
                    Title = L10n.T("Cli.HelpTitle"),
                    Content = new ScrollViewer { Content = text, MaxHeight = 420 },
                    CloseButtonText = L10n.T("Cli.Close"),
                    DefaultButton = ContentDialogButton.Close
                };
                ApplyDialogTheme(dialog);
                await dialog.ShowAsync();
            }
            catch (Exception ex) { App.WriteLog("Command-line help", ex); }
            finally
            {
                _dialogOpen = false;
                if (!_closed) RefreshUpdatePresentation();
            }
        }

        // ------------------------------------------------------------------ desktop shortcuts
        private void OnCreateShortcutClick(object sender, RoutedEventArgs e)
        {
            if (_closed || _closePending) return;
            SyncConfig();
            try
            {
                string executable = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "HNAUnlocker.exe");
                // Name the client only when there is a choice, so a moved game does not break the shortcut.
                string game = (_config.GamePaths?.Length ?? 0) > 1 && IsValidGamePath(_config.ProcessPath)
                    ? _config.ProcessPath : null;
                var options = CommandLineOptions.ForShortcut(_config.TargetFramerate, _config.FovDelta,
                    _config.DisableCameraFade, game);
                string arguments = CommandLineOptions.Join(options.ToArguments());
                ShortcutResult result = DesktopShortcuts.Ensure(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) },
                    executable, options,
                    L10n.T("Cli.ShortcutName", options.Fps),
                    L10n.T("Cli.ShortcutDescription", arguments));
                string name = Path.GetFileNameWithoutExtension(result.Path);
                bool created = result.Outcome == ShortcutOutcome.Created;
                ShortcutInfoBar.Severity = created ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
                ShortcutInfoBar.Title = L10n.T(created ? "Cli.ShortcutCreatedTitle" : "Cli.ShortcutExistsTitle");
                ShortcutInfoBar.Message = L10n.T(created ? "Cli.ShortcutCreated" : "Cli.ShortcutExists", name, arguments);
                Log(L10n.T(created ? "Cli.LogShortcutCreated" : "Cli.LogShortcutExists", result.Path));
            }
            catch (Exception ex)
            {
                ShortcutInfoBar.Severity = InfoBarSeverity.Error;
                ShortcutInfoBar.Title = L10n.T("Cli.ShortcutFailedTitle");
                ShortcutInfoBar.Message = L10n.T("Cli.ShortcutFailed");
                App.WriteLog("Desktop shortcut", ex);
            }
            ShortcutInfoBar.IsOpen = true;
        }
    }
}
