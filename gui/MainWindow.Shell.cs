using System;
using System.Runtime.InteropServices;
using HNAUnlocker.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HNAUnlocker
{
    /// <summary>Window chrome: theme, title bar, backdrop, page navigation, and the status badge.</summary>
    public sealed partial class MainWindow
    {
        private const string HomePageTag = "home", LogPageTag = "log", SettingsPageTag = "settings";
        private string _currentPage = HomePageTag;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private const int DwmUseImmersiveDarkMode = 20;

        private void ConfigureChrome()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            if (AppWindowTitleBar.IsCustomizationSupported())
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
                LayoutRoot.Background = null;
            }
            LayoutRoot.ActualThemeChanged += (_, __) => ApplyTitleBarTheme();
            ApplyTheme();
        }

        private static ElementTheme ToElementTheme(AppThemePreference theme) => theme switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        private void ApplyTheme()
        {
            LayoutRoot.RequestedTheme = ToElementTheme(_config.Theme);
            ApplyTitleBarTheme();
        }

        /// <summary>Caption buttons and the window frame are drawn by Windows, so recolor them per theme.</summary>
        private void ApplyTitleBarTheme()
        {
            if (_closed) return;
            bool dark = LayoutRoot.ActualTheme == ElementTheme.Dark;
            try
            {
                if (AppWindowTitleBar.IsCustomizationSupported())
                {
                    var bar = AppWindow.TitleBar;
                    Color foreground = dark ? Colors.White : Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
                    bar.ButtonBackgroundColor = Colors.Transparent;
                    bar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    bar.ButtonForegroundColor = foreground;
                    bar.ButtonInactiveForegroundColor = dark
                        ? Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A) : Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);
                    bar.ButtonHoverForegroundColor = foreground;
                    bar.ButtonHoverBackgroundColor = dark
                        ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
                    bar.ButtonPressedForegroundColor = foreground;
                    bar.ButtonPressedBackgroundColor = dark
                        ? Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
                }
                int useDark = dark ? 1 : 0;
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));
            }
            catch (Exception ex) { App.WriteLog("Title bar theme", ex); }
        }

        private void UpdateTitleBarActivation(bool active) =>
            TitleBarText.Opacity = VersionText.Opacity = active ? 1.0 : 0.5;

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || _updatingLanguage || _closed || _closePending ||
                ThemeBox.SelectedIndex is < 0 or > 2) return;
            var theme = (AppThemePreference)ThemeBox.SelectedIndex;
            if (_config.Theme == theme) return;
            _config.Theme = theme;
            ApplyTheme();
            if (!_config.Save()) Log(L10n.T("Ui.LogConfigSaveFailed", UnlockConfig.LogPath));
        }

        /// <summary>ContentDialogs live in the popup layer and do not inherit the root's requested theme.</summary>
        private void ApplyDialogTheme(ContentDialog dialog) => dialog.RequestedTheme = LayoutRoot.ActualTheme;

        // ------------------------------------------------------------------ navigation
        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag) ShowPage(tag);
        }

        private void ShowPage(string tag)
        {
            _currentPage = tag;
            HomePage.Visibility = tag == HomePageTag ? Visibility.Visible : Visibility.Collapsed;
            LogPage.Visibility = tag == LogPageTag ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = tag == SettingsPageTag ? Visibility.Visible : Visibility.Collapsed;
            if (tag == LogPageTag) RenderLog(forceFollow: true);
        }

        private bool LogPageVisible => _currentPage == LogPageTag;

        private void NavigateToUpdates(Action afterLayout)
        {
            NavView.SelectedItem = NavSettingsItem;
            ShowPage(SettingsPageTag);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_closed) return;
                UpdatesSection.StartBringIntoView();
                afterLayout?.Invoke();
            });
        }

        // ------------------------------------------------------------------ status badge
        private void SetStatusBadge(string glyph)
        {
            bool success = glyph == "", critical = glyph == "", busy = glyph == "";
            StatusIcon.Glyph = glyph;
            BadgeNeutral.Visibility = success || critical || busy ? Visibility.Collapsed : Visibility.Visible;
            BadgeSuccess.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
            BadgeCritical.Visibility = critical ? Visibility.Visible : Visibility.Collapsed;
            BadgeBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (StatusProgress.IsActive != busy) StatusProgress.IsActive = busy;
        }
    }
}
