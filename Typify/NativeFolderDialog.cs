using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Typify
{
    public sealed class NativeFolderDialog : IDisposable
    {
        private const int Win32ErrorCancelled = unchecked((int)0x800704C7);

        private static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

        private IFileOpenDialog _dialog;

        public string Title { get; set; }
        public string InitialDirectory { get; set; }
        public string SelectedPath { get; private set; }

        public bool ShowDialog()
        {
            return ShowDialog(IntPtr.Zero);
        }

        public bool ShowDialog(Window ownerWindow)
        {
            if (ownerWindow == null)
                return ShowDialog(IntPtr.Zero);

            return ShowDialog(new WindowInteropHelper(ownerWindow).Handle);
        }

        public bool ShowDialog(IntPtr ownerHandle)
        {
            SelectedPath = null;

            try
            {
                _dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidFileOpenDialog));
                if (_dialog == null)
                    return false;

                int hr = _dialog.GetOptions(out FILEOPENDIALOGOPTIONS existingOptions);
                if (hr != 0)
                    return false;

                FILEOPENDIALOGOPTIONS requiredOptions = FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS |
                    FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM |
                    FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST;

                hr = _dialog.SetOptions(existingOptions | requiredOptions);
                if (hr != 0)
                    return false;

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    hr = _dialog.SetTitle(Title);
                    if (hr != 0)
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(InitialDirectory) && Directory.Exists(InitialDirectory))
                {
                    int initialFolderHr = SHCreateItemFromParsingName(InitialDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem initialFolder);
                    if (initialFolderHr == 0 && initialFolder != null)
                    {
                        try
                        {
                            hr = _dialog.SetFolder(initialFolder);
                            if (hr != 0)
                                return false;
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(initialFolder);
                        }
                    }
                }

                hr = _dialog.Show(ownerHandle);
                if (hr == Win32ErrorCancelled)
                    return false;

                if (hr != 0)
                    return false;

                hr = _dialog.GetResult(out IShellItem selectedItem);
                if (hr != 0 || selectedItem == null)
                    return false;

                try
                {
                    hr = selectedItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr filePathPointer);
                    if (hr != 0 || filePathPointer == IntPtr.Zero)
                        return false;

                    try
                    {
                        SelectedPath = Marshal.PtrToStringUni(filePathPointer);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(filePathPointer);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(selectedItem);
                }

                return !string.IsNullOrWhiteSpace(SelectedPath);
            }
            finally
            {
                if (_dialog != null)
                {
                    Marshal.ReleaseComObject(_dialog);
                    _dialog = null;
                }
            }
        }

        public void Dispose()
        {
            if (_dialog != null)
            {
                Marshal.ReleaseComObject(_dialog);
                _dialog = null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [Flags]
        private enum FILEOPENDIALOGOPTIONS : uint
        {
            FOS_PICKFOLDERS = 0x20,
            FOS_FORCEFILESYSTEM = 0x40,
            FOS_PATHMUSTEXIST = 0x800,
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            [PreserveSig] int SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            [PreserveSig] int SetFileTypeIndex(uint iFileType);
            [PreserveSig] int GetFileTypeIndex(out uint piFileType);
            [PreserveSig] int Advise(IntPtr pfde, out uint pdwCookie);
            [PreserveSig] int Unadvise(uint dwCookie);
            [PreserveSig] int SetOptions(FILEOPENDIALOGOPTIONS fos);
            [PreserveSig] int GetOptions(out FILEOPENDIALOGOPTIONS pfos);
            [PreserveSig] int SetDefaultFolder(IShellItem psi);
            [PreserveSig] int SetFolder(IShellItem psi);
            [PreserveSig] int GetFolder(out IShellItem ppsi);
            [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
            [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            [PreserveSig] int GetResult(out IShellItem ppsi);
            [PreserveSig] int AddPlace(IShellItem psi, int fdap);
            [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            [PreserveSig] int Close(int hr);
            [PreserveSig] int SetClientGuid(ref Guid guid);
            [PreserveSig] int ClearClientData();
            [PreserveSig] int SetFilter(IntPtr pFilter);
            [PreserveSig] int GetResults(out IntPtr ppenum);
            [PreserveSig] int GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            [PreserveSig] int GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
