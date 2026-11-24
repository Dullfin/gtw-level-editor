using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GTWLevelEditor
{
    /// <summary>
    /// The Windows open/save file dialogs and the Recycle Bin, for importing, exporting and deleting
    /// levels. Get To Work is Windows-only, so these call the Win32 API directly. The dialogs are modal:
    /// the game pauses until the player closes them.
    /// </summary>
    internal static class FileDialogs
    {
        private const string LevelFilter = "Level files (*.json)\0*.json\0All files (*.*)\0*.*\0\0";

        /// <summary>Asks for a level file to open. Returns null if the player cancels.</summary>
        public static string OpenLevel(string title)
        {
            return Show(title, null, open: true);
        }

        /// <summary>Asks where to save a level file, starting from <paramref name="fileName"/>. Returns null if the player cancels.</summary>
        public static string SaveLevel(string title, string fileName)
        {
            return Show(title, fileName, open: false);
        }

        private static string Show(string title, string fileName, bool open)
        {
            const int maxChars = 4096;
            var buffer = Marshal.AllocHGlobal(maxChars * 2);
            try
            {
                var initial = (fileName ?? "") + "\0";
                if (initial.Length > maxChars) initial = initial.Substring(0, maxChars - 1) + "\0";
                Marshal.Copy(initial.ToCharArray(), 0, buffer, initial.Length);

                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = LevelFilter,
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = maxChars,
                    lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    lpstrTitle = title,
                    lpstrDefExt = "json",
                    Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR |
                            (open ? OFN_FILEMUSTEXIST : OFN_OVERWRITEPROMPT),
                };
                bool ok = open ? GetOpenFileName(ref ofn) : GetSaveFileName(ref ofn);
                if (!ok) return null;
                var path = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Moves a file to the Recycle Bin, so a deleted level can still be restored.</summary>
        public static bool Recycle(string path)
        {
            if (!File.Exists(path)) return false;
            var op = new ShFileOpStruct
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = Path.GetFullPath(path) + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
        }

        private const int OFN_OVERWRITEPROMPT = 0x2;
        private const int OFN_NOCHANGEDIR = 0x8;
        private const int OFN_PATHMUSTEXIST = 0x800;
        private const int OFN_FILEMUSTEXIST = 0x1000;
        private const int OFN_EXPLORER = 0x80000;

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x4;
        private const ushort FOF_NOCONFIRMATION = 0x10;
        private const ushort FOF_ALLOWUNDO = 0x40;
        private const ushort FOF_NOERRORUI = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileOpStruct
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
        private static extern bool GetOpenFileName(ref OpenFileName ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW", SetLastError = true)]
        private static extern bool GetSaveFileName(ref OpenFileName ofn);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int SHFileOperation(ref ShFileOpStruct op);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
