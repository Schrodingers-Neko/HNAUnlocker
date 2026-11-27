using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using HNAUnlocker.Core;

namespace HNAUnlocker
{
    public sealed partial class MainWindow
    {
        private bool _updatingLanguage;

        private void CreateFpsPresets()
        {
            if (PresetPanel.Children.Count != 0) return;
            for (int i = 0; i < FpsPresets.Length; i++)
            {
                int value = FpsPresets[i];
                var button = new ToggleButton
                {
                    Content = value.ToString(), Tag = value, MinWidth = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(4, 6, 4, 6)
                };
                Grid.SetColumn(button, i);
                AutomationProperties.SetAutomationId(button, "FpsPreset" + value);
                button.Click += OnFpsPresetClick;
                PresetPanel.Children.Add(button);
            }
        }

        private void ApplyLocalization()
        {
            bool wasInitializing = _initializing;
            _initializing = true;
            _updatingLanguage = true;
            try
            {
                LayoutRoot.Language = L10n.Language;
                Title = L10n.T("Ui.WindowTitle", AppVersion);
                VersionText.Text = L10n.T("Ui.Version", AppVersion);
                NavHomeItem.Content = L10n.T("Ui.NavHome");
                NavLogItem.Content = L10n.T("Ui.RuntimeLog");
                NavSettingsItem.Content = L10n.T("Ui.NavSettings");
                HomePageTitle.Text = L10n.T("Ui.NavHome");
                LogPageTitle.Text = L10n.T("Ui.RuntimeLog");
                SettingsPageTitle.Text = L10n.T("Ui.NavSettings");
                SaveButtonText.Text = L10n.T("Ui.SaveConfig");
                AppearanceSectionTitle.Text = L10n.T("Ui.AppearanceSection");
                ThemeLabel.Text = L10n.T("Ui.Theme");
                ThemeHint.Text = L10n.T("Ui.ThemeTip");
                LanguageLabel.Text = L10n.T("Ui.Language");
                LanguageHint.Text = L10n.T("Ui.LanguageTip");
                AboutSectionTitle.Text = L10n.T("Ui.AboutSection");
                AboutTitle.Text = L10n.T("Ui.WindowTitle", AppVersion);
                AboutDescription.Text = L10n.T("Ui.AboutDescription");
                SessionFailureInfoBar.Title = L10n.T("Ui.SessionFailureReportTitle");
                RefreshSessionFailureReason();
                SessionFailureReportHint.Text = L10n.T("Ui.SessionFailureReportMessage");
                SessionFailureExportButton.Content = WrappedText("Ui.ExportDiagnostics");
                LocalizeControl(SessionFailureExportButton, "Ui.ExportDiagnostics", "Ui.ExportDiagnosticsTip");
                SessionFailureReportLink.Content = WrappedText("Ui.SessionFailureReportAction");
                LocalizeControl(SessionFailureReportLink, "Ui.SessionFailureReportAction");
                PathLabel.Text = L10n.T("Ui.Client");
                ClientPathBox.PlaceholderText = L10n.T("Ui.ChooseExecutable");
                DisplaySectionTitle.Text = L10n.T("Ui.DisplaySection");
                FpsLabel.Text = L10n.T("Ui.TargetFramerate");
                FpsUnitText.Text = L10n.T("Ui.FpsUnit");
                FovLabel.Text = L10n.T("Ui.FovDelta");
                AntiFadeLabel.Text = L10n.T("Ui.AntiFade");
                StartupSectionTitle.Text = L10n.T("Ui.StartupSection");
                MobileUiLabel.Text = L10n.T("Ui.MobileUi");
                MobileUiHint.Text = L10n.T("Ui.NextGameLaunch");
                MobileUiDisabledReason.Text = L10n.T("Ui.MobileUiDisabledReason");
                AutoStartLabel.Text = L10n.T("Ui.AutoStart");
                AutoStartHint.Text = L10n.T("Ui.AutoStartTip");
                StartMinimizedLabel.Text = L10n.T("Ui.StartMinimized");
                StartMinimizedHint.Text = L10n.T("Ui.StartMinimizedTip");
                ExitWithGameLabel.Text = L10n.T("Updates.ExitWithGame");
                ExitWithGameHint.Text = L10n.T("Updates.ExitWithGameTip");
                UpdatesSectionTitle.Text = L10n.T("Updates.Section");
                CheckForUpdatesLabel.Text = L10n.T("Updates.CheckOnStartup");
                CheckForUpdatesHint.Text = L10n.T("Updates.CheckOnStartupTip");
                UpdateSourceLabel.Text = L10n.T("Updates.Source");
                UpdateSourceHint.Text = L10n.T("Updates.SourceTip");
                StartButtonText.Text = L10n.T("Ui.LaunchGame");
                AttachButtonText.Text = L10n.T("Ui.AttachGame");
                StopButtonText.Text = L10n.T("Ui.StopRestore");
                // WinUI caches selection text; reselect while change handlers are guarded.
                int graphicsSourceIndex = GraphicsSourceBox.SelectedIndex;
                GraphicsSourceBox.SelectedItem = null;
                GraphicsSourceBox.Header = WrappedText("Ui.GraphicsSource");
                GraphicsAutoItem.Content = L10n.T("Ui.GraphicsAuto");
                GraphicsChinaItem.Content = L10n.T("Ui.GraphicsChina");
                GraphicsGlobalItem.Content = L10n.T("Ui.GraphicsGlobal");
                GraphicsSourceBox.SelectedIndex = graphicsSourceIndex;
                int safeAreaModeIndex = SafeAreaModeBox.SelectedIndex;
                SafeAreaModeBox.SelectedItem = null;
                SafeAreaModeBox.Header = WrappedText("Ui.SafeAreaMode");
                SafeAreaOriginalItem.Content = L10n.T("Ui.SafeAreaOriginal");
                SafeAreaCustomItem.Content = L10n.T("Ui.SafeAreaCustom");
                SafeAreaModeBox.SelectedIndex = safeAreaModeIndex;
                int updateSourceIndex = UpdateSourceBox.SelectedIndex;
                UpdateSourceBox.SelectedItem = null;
                UpdateSourceAutoItem.Content = L10n.T("Updates.SourceAuto");
                UpdateSourceGitHubItem.Content = L10n.T("Updates.SourceGitHub");
                // Gitee mirror disabled: the Gitee choice is no longer listed.
                // UpdateSourceGiteeItem.Content = L10n.T("Updates.SourceGitee");
                UpdateSourceBox.SelectedIndex = updateSourceIndex;
                int themeIndex = ThemeBox.SelectedIndex;
                ThemeBox.SelectedItem = null;
                ThemeSystemItem.Content = L10n.T("Ui.ThemeSystem");
                ThemeLightItem.Content = L10n.T("Ui.ThemeLight");
                ThemeDarkItem.Content = L10n.T("Ui.ThemeDark");
                ThemeBox.SelectedIndex = themeIndex;
                SafeAreaHorizontalBox.Header = WrappedText("Ui.SafeAreaHorizontal");
                LanguageBox.SelectedItem = null;
                FontCreditText.Text = L10n.T("Ui.FontCredit");
                LanguageAutoItem.Content = L10n.T("Ui.LanguageAuto");
                foreach (var item in LanguageBox.Items)
                    if (item is ComboBoxItem choice && choice.Tag is string preference &&
                        preference == L10n.Preference)
                    {
                        LanguageBox.SelectedItem = choice;
                        break;
                    }

                LocalizeControl(SaveButton, "Ui.SaveConfig", "Ui.SaveConfig");
                LocalizeControl(ClientPathBox, "Ui.GamePath");
                LocalizeControl(BrowseButton, "Ui.ChooseExecutable", "Ui.ChooseExecutable");
                LocalizeControl(FpsSlider, "Ui.FpsSliderName", "Ui.FpsSliderTip");
                LocalizeControl(FpsBox, "Ui.FpsBoxName", "Ui.FpsBoxTip");
                LocalizeControl(FovSlider, "Ui.FovSliderName", "Ui.FovSliderTip");
                LocalizeControl(FovBox, "Ui.FovBoxName", "Ui.FovBoxTip");
                LocalizeControl(ResetFovButton, "Ui.ResetFov", "Ui.ResetFovTip");
                LocalizeControl(AntiFadeToggle, "Ui.AntiFade", "Ui.AntiFadeTip");
                LocalizeControl(MobileUiToggle, "Ui.MobileUi", "Ui.MobileUiTip");
                LocalizeControl(GraphicsSourceBox, "Ui.GraphicsSourceName", "Ui.GraphicsSourceTip");
                LocalizeControl(SafeAreaModeBox, "Ui.SafeAreaMode", "Ui.SafeAreaModeTip");
                LocalizeControl(SafeAreaHorizontalBox, "Ui.SafeAreaHorizontal", "Ui.SafeAreaHorizontalTip");
                LocalizeControl(AutoStartToggle, "Ui.AutoStart", "Ui.AutoStartTip");
                LocalizeControl(StartMinimizedToggle, "Ui.StartMinimized", "Ui.StartMinimizedTip");
                LocalizeControl(ExitWithGameToggle, "Updates.ExitWithGame", "Updates.ExitWithGameTip");
                LocalizeControl(CheckForUpdatesToggle, "Updates.CheckOnStartup", "Updates.CheckOnStartupTip");
                LocalizeControl(UpdateSourceBox, "Updates.Source", "Updates.SourceTip");
                ReviewUpdateNoticeButton.Content = WrappedText("Updates.Review");
                CheckUpdatesButton.Content = WrappedText("Updates.CheckNow");
                OpenReleaseButton.Content = WrappedText("Updates.OpenRelease");
                CancelUpdateButton.Content = WrappedText("Updates.Cancel");
                LocalizeControl(CheckUpdatesButton, "Updates.CheckNow", "Updates.CheckNow");
                LocalizeControl(OpenReleaseButton, "Updates.OpenRelease", "Updates.OpenRelease");
                LocalizeControl(ApplyUpdateButton, "Updates.Install", "Updates.InstallTip");
                LocalizeControl(CancelUpdateButton, "Updates.Cancel", "Updates.Cancel");
                LocalizeControl(UpdateProgressBar, "Updates.Progress");
                LocalizeControl(LanguageBox, "Ui.Language", "Ui.LanguageTip");
                LocalizeControl(ThemeBox, "Ui.Theme", "Ui.ThemeTip");
                LocalizeControl(LogText, "Ui.RuntimeLogContent");
                ExportDiagnosticsButton.Content = WrappedText("Ui.ExportDiagnostics");
                LocalizeControl(ExportDiagnosticsButton, "Ui.ExportDiagnostics", "Ui.ExportDiagnosticsTip");
                RefreshDiagnosticsExport();
                LocalizeControl(CopyLogButton, "Ui.CopyLog", "Ui.CopyVisibleLog");
                LocalizeControl(ClearLogButton, "Ui.ClearVisibleLog", "Ui.ClearVisibleLog");
                LocalizeControl(StartButton, "Ui.LaunchGame", "Ui.LaunchGameTip");
                LocalizeControl(AttachButton, "Ui.AttachGame", "Ui.AttachGameTip");
                LocalizeControl(StopButton, "Ui.StopRestore", "Ui.StopRestoreTip");
                foreach (var child in PresetPanel.Children)
                    if (child is ToggleButton button && button.Tag is int value)
                    {
                        string text = L10n.T("Ui.FpsPreset", value);
                        AutomationProperties.SetName(button, text);
                        SetWrappedTooltip(button, text);
                    }
                RefreshUpdatePresentation();
                RefreshClientPresentation();
            }
            finally
            {
                _updatingLanguage = false;
                _initializing = wasInitializing;
            }
        }

        private static TextBlock WrappedText(string key) => new TextBlock
        {
            Text = L10n.T(key),
            TextWrapping = TextWrapping.Wrap
        };

        private static void LocalizeControl(DependencyObject control, string nameKey, string tooltipKey = null)
        {
            AutomationProperties.SetName(control, L10n.T(nameKey));
            if (tooltipKey != null) SetWrappedTooltip(control, L10n.T(tooltipKey));
        }

        private static void SetWrappedTooltip(DependencyObject control, string text) =>
            ToolTipService.SetToolTip(control, new ToolTip
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 }
            });

        private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _updatingLanguage || _closed || _closePending ||
                LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string preference)
                return;
            preference = L10n.NormalizePreference(preference);
            if (preference == L10n.Preference) return;
            _config.Language = preference;
            L10n.SetLanguage(preference);
            ApplyLocalization();
            RefreshPresentation();
            if (!_config.Save()) Log(L10n.T("Ui.LogConfigSaveFailed", UnlockConfig.LogPath));
        }

        private static string SessionStateText(SessionState state) => L10n.T(state switch
        {
            SessionState.WaitingForGame => "Ui.StateWaiting",
            SessionState.Patching => "Ui.StateAttaching",
            SessionState.Active => "Ui.StateActive",
            SessionState.Failed => "Ui.StateFailed",
            SessionState.GameExited => "Ui.StateGameExited",
            _ => "Ui.StateIdle"
        });
    }
}
