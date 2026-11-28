using System;
using System.IO;
using System.Runtime.InteropServices;
using HNAUnlocker.Core;

namespace HNAUnlocker
{
    /// <summary>Native notification area integration; the application remains WinUI 3.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const uint Callback = 0x8000 + 51;
        private const uint CopyData = 0x004A;
        // Marks a WM_COPYDATA payload as a forwarded HNAUnlocker command line ("HNA1").
        private const nuint CommandLinePayload = 0x31414E48;
        private const int MaxCommandLineBytes = 64 * 1024;
        private const nuint Id = 1;
        private const string WindowProperty = "HNAUnlocker.MainWindow.61134DCF-DF8D-461B-B785-3F546EE26AA7";
        private readonly IntPtr _window;
        private readonly SubclassProc _callback;
        private readonly Action _restore, _stop, _exit;
        private readonly Action<string[]> _commandLine;
        private bool _added, _disposed;
        private IntPtr _icon;
        private readonly bool _ownsIcon;
        private readonly uint _taskbarCreated;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint subclass, nuint data);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Message;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct CopyDataStruct { public nuint Data; public uint Size; public IntPtr Buffer; }
        [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id, nuint data);
        [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id);
        [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, nuint wparam, nint lparam);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint action, ref NotifyData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string message);
        [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string text);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetPropW(IntPtr hwnd, string name, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetPropW(IntPtr hwnd, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemovePropW(IntPtr hwnd, string name);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern IntPtr SendCopyDataTimeout(IntPtr hwnd, uint message, nuint wparam, ref CopyDataStruct data, uint flags, uint timeout, out nuint result);
        /// <summary>Brings the running copy forward, first handing it this copy's command line, if any.</summary>
        internal static void RestoreExisting(System.Collections.Generic.IReadOnlyList<string> args = null)
        {
            IntPtr hwnd = IntPtr.Zero;
            EnumWindows((candidate, _) =>
            {
                if (GetPropW(candidate, WindowProperty) == IntPtr.Zero) return true;
                hwnd = candidate;
                return false;
            }, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;
            if (args != null && args.Count > 0) ForwardCommandLine(hwnd, args);
            SendMessageTimeoutW(hwnd, Callback, 0, (nint)((1 << 16) | 0x400), 2, 1000, out _);
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }

        private static void ForwardCommandLine(IntPtr hwnd, System.Collections.Generic.IReadOnlyList<string> args)
        {
            string text = string.Join("\0", args);
            if ((long)text.Length * 2 > MaxCommandLineBytes) return;
            IntPtr buffer = Marshal.StringToHGlobalUni(text);
            try
            {
                var data = new CopyDataStruct { Data = CommandLinePayload, Size = (uint)(text.Length * 2), Buffer = buffer };
                SendCopyDataTimeout(hwnd, CopyData, 0, ref data, 2 /* SMTO_ABORTIFHUNG */, 2000, out _);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public TrayIcon(IntPtr window, Action restore, Action stop, Action exit, Action<string[]> commandLine = null)
        {
            _window = window; _restore = restore; _stop = stop; _exit = exit; _commandLine = commandLine;
            _callback = OnMessage;
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            if (!SetWindowSubclass(window, _callback, Id, 0))
                throw new InvalidOperationException(L10n.T("Tray.RegisterCallbackFailed"));
            if (!SetPropW(window, WindowProperty, new IntPtr(1)))
            {
                int error = Marshal.GetLastWin32Error();
                RemoveWindowSubclass(window, _callback, Id);
                throw new System.ComponentModel.Win32Exception(error, L10n.T("Tray.RegisterWindowFailed"));
            }
            _icon = LoadImageW(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                1, 0, 0, 0x10 | 0x40); // IMAGE_ICON; LR_LOADFROMFILE | LR_DEFAULTSIZE, owned handle.
            _ownsIcon = _icon != IntPtr.Zero;
            if (!_ownsIcon)
            {
                int error = Marshal.GetLastWin32Error();
                App.WriteLog("Tray icon", new System.ComponentModel.Win32Exception(error, L10n.T("Tray.LoadIconFailed")));
                _icon = LoadIconW(IntPtr.Zero, (IntPtr)32512);
            }
        }

        private NotifyData Data(uint flags = 7) => new NotifyData
        {
            Size = (uint)Marshal.SizeOf<NotifyData>(), Window = _window, Id = 1,
            Flags = flags, Message = Callback, Icon = _icon,
            Tip = "HNAUnlocker", Info = "", Title = "",
        };

        public bool Add()
        {
            if (_added) return true;
            var data = Data();
            _added = Shell_NotifyIconW(0, ref data);
            if (_added) { data.Version = 4; Shell_NotifyIconW(4, ref data); }
            return _added;
        }

        public void HideWindow()
        {
            if (!Add()) throw new InvalidOperationException(L10n.T("Tray.CreateIconFailed"));
            ShowWindow(_window, 0);
            var balloon = Data(0x10);
            balloon.Info = L10n.T("Tray.HiddenBalloon");
            balloon.Title = "HNAUnlocker";
            balloon.InfoFlags = 1;
            Shell_NotifyIconW(1, ref balloon);
        }

        public void RestoreWindow()
        {
            ShowWindow(_window, 9);
            SetForegroundWindow(_window);
            _restore();
        }

        private void ContextMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;
            try
            {
                AppendMenuW(menu, 0, 1, L10n.T("Tray.Open"));
                AppendMenuW(menu, 0, 2, L10n.T("Tray.StopRestore"));
                AppendMenuW(menu, 0, 3, L10n.T("Tray.Exit"));
                GetCursorPos(out var cursor);
                SetForegroundWindow(_window);
                uint selected = TrackPopupMenu(menu, 0x100 | 0x2, cursor.X, cursor.Y, 0, _window, IntPtr.Zero);
                if (selected == 1) RestoreWindow();
                else if (selected == 2) _stop();
                else if (selected == 3) _exit();
            }
            finally { DestroyMenu(menu); }
        }

        private IntPtr OnMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint subclass, nuint data)
        {
            try
            {
                if (message == _taskbarCreated && _added) { _added = false; Add(); }
                if (message == CopyData && lparam != 0)
                {
                    var copy = Marshal.PtrToStructure<CopyDataStruct>(lparam);
                    if (copy.Data == CommandLinePayload)
                    {
                        if (_commandLine != null && copy.Buffer != IntPtr.Zero && copy.Size % 2 == 0
                            && copy.Size <= MaxCommandLineBytes)
                        {
                            // Copy now: the sender frees the buffer as soon as this message returns.
                            string text = copy.Size == 0 ? string.Empty : Marshal.PtrToStringUni(copy.Buffer, (int)(copy.Size / 2));
                            _commandLine(text.Length == 0 ? Array.Empty<string>() : text.Split('\0'));
                        }
                        return (IntPtr)1;
                    }
                }
                if (message == Callback)
                {
                    uint notification = (uint)(lparam.ToInt64() & 0xFFFF);
                    if (notification == 0x202 || notification == 0x203 || notification == 0x400 || notification == 0x401)
                        RestoreWindow();
                    else if (notification == 0x205 || notification == 0x7B) ContextMenu();
                }
            }
            catch (Exception ex) { App.WriteLog("Tray callback", ex); }
            return DefSubclassProc(hwnd, message, wparam, lparam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RemovePropW(_window, WindowProperty);
            if (_added) { var data = Data(0); Shell_NotifyIconW(2, ref data); _added = false; }
            RemoveWindowSubclass(_window, _callback, Id);
            if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            GC.KeepAlive(_callback);
        }
    }
}
