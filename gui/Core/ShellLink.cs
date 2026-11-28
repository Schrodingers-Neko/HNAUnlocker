using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace HNAUnlocker.Core
{
    /// <summary>Reads and writes Windows .lnk files through the shell's own IShellLinkW.</summary>
    internal static class ShellLink
    {
        internal sealed class Info
        {
            public string Target { get; init; } = string.Empty;
            public string Arguments { get; init; } = string.Empty;
        }

        internal static Info Read(string path)
        {
            object link = CreateLink();
            try
            {
                ((IPersistFile)link).Load(path, 0 /* STGM_READ */);
                var shell = (IShellLinkW)link;
                var target = new StringBuilder(32768);
                shell.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(32768);
                shell.GetArguments(arguments, arguments.Capacity);
                return new Info { Target = target.ToString(), Arguments = arguments.ToString() };
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        internal static void Write(string path, string target, string arguments, string workingDirectory,
            string description)
        {
            object link = CreateLink();
            try
            {
                var shell = (IShellLinkW)link;
                shell.SetPath(target);
                shell.SetArguments(arguments ?? string.Empty);
                shell.SetWorkingDirectory(workingDirectory ?? string.Empty);
                shell.SetIconLocation(target, 0);
                if (!string.IsNullOrEmpty(description))
                    shell.SetDescription(description.Length > 259 ? description.Substring(0, 259) : description);
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        private static object CreateLink() =>
            Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true));

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
