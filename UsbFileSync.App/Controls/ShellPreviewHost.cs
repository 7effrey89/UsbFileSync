using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using UsbFileSync.App.Services;

namespace UsbFileSync.App.Controls;

public sealed class ShellPreviewHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const uint StgmRead = 0x00000000;
    private const uint StgmShareDenyNone = 0x00000040;

    private readonly IShellPreviewHandlerResolver _previewHandlerResolver = new WindowsShellPreviewHandlerResolver();
    private IntPtr _hwnd;
    private ShellPreviewSession? _session;

    public bool TryLoadPreview(string filePath, out string errorMessage)
    {
        errorMessage = string.Empty;
        ClearPreview();

        if (string.IsNullOrWhiteSpace(filePath) || _hwnd == IntPtr.Zero)
        {
            errorMessage = "The preview surface is not available.";
            return false;
        }

        if (!_previewHandlerResolver.TryGetPreviewHandlerClsid(filePath, out var previewHandlerClsid))
        {
            errorMessage = "No registered Windows preview handler is available for this file type.";
            return false;
        }

        try
        {
            _session = ShellPreviewSession.Create(previewHandlerClsid, filePath, _hwnd, GetClientRect(_hwnd));
            return true;
        }
        catch (Exception exception)
        {
            ClearPreview();
            errorMessage = exception.Message;
            return false;
        }
    }

    public void ClearPreview()
    {
        _session?.Dispose();
        _session = null;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = NativeMethods.CreateWindowEx(
            0,
            "Static",
            string.Empty,
            WsChild | WsVisible,
            0,
            0,
            0,
            0,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ClearPreview();

        if (hwnd.Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(hwnd.Handle);
        }

        _hwnd = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);

        if (_hwnd != IntPtr.Zero)
        {
            _session?.SetRect(GetClientRect(_hwnd));
        }
    }

    private static NativeRect GetClientRect(IntPtr hwnd)
    {
        NativeMethods.GetClientRect(hwnd, out var rect);
        return rect;
    }

    private sealed class ShellPreviewSession : IDisposable
    {
        private readonly object _comObject;
        private readonly IPreviewHandler _previewHandler;
        private readonly IStream? _stream;

        private ShellPreviewSession(object comObject, IPreviewHandler previewHandler, IStream? stream)
        {
            _comObject = comObject;
            _previewHandler = previewHandler;
            _stream = stream;
        }

        public static ShellPreviewSession Create(Guid previewHandlerClsid, string filePath, IntPtr hostHandle, NativeRect rect)
        {
            Exception? lastFailure = null;

            foreach (var initializationMode in new[] { PreviewInitializationMode.File, PreviewInitializationMode.Stream })
            {
                if (TryCreate(previewHandlerClsid, filePath, hostHandle, rect, initializationMode, out var session, out var failure))
                {
                    return session!;
                }

                if (failure is not null)
                {
                    lastFailure = failure;
                }
            }

            throw lastFailure ?? new InvalidOperationException("The preview handler could not be initialized.");
        }

        public void SetRect(NativeRect rect)
        {
            _previewHandler.SetRect(ref rect);
        }

        public void Dispose()
        {
            TryUnload(_previewHandler);
            ReleaseComObject(_stream);
            ReleaseComObject(_comObject);
        }

        private static bool TryCreate(
            Guid previewHandlerClsid,
            string filePath,
            IntPtr hostHandle,
            NativeRect rect,
            PreviewInitializationMode initializationMode,
            out ShellPreviewSession? session,
            out Exception? failure)
        {
            session = null;
            failure = null;

            var comType = Type.GetTypeFromCLSID(previewHandlerClsid, throwOnError: true)!;
            var comObject = Activator.CreateInstance(comType) ?? throw new InvalidOperationException("The preview handler could not be created.");
            var previewHandler = (IPreviewHandler)comObject;
            IStream? stream = null;

            try
            {
                if (!Initialize(comObject, filePath, initializationMode, out stream))
                {
                    ReleaseComObject(comObject);
                    return false;
                }

                previewHandler.SetWindow(hostHandle, ref rect);
                previewHandler.SetRect(ref rect);
                previewHandler.DoPreview();
                previewHandler.SetRect(ref rect);
                session = new ShellPreviewSession(comObject, previewHandler, stream);
                return true;
            }
            catch (Exception exception)
            {
                failure = exception;
                ReleaseComObject(stream);
                TryUnload(previewHandler);
                ReleaseComObject(comObject);
                return false;
            }
        }

        private static bool Initialize(object comObject, string filePath, PreviewInitializationMode initializationMode, out IStream? stream)
        {
            stream = null;
            const uint readOnlyMode = StgmRead | StgmShareDenyNone;

            if (initializationMode == PreviewInitializationMode.File)
            {
                if (comObject is not IInitializeWithFile initializeWithFile)
                {
                    return false;
                }

                initializeWithFile.Initialize(filePath, readOnlyMode);
                return true;
            }

            if (comObject is not IInitializeWithStream initializeWithStream)
            {
                return false;
            }

            NativeMethods.SHCreateStreamOnFileEx(filePath, readOnlyMode, 0, false, null, out stream);
            initializeWithStream.Initialize(stream, readOnlyMode);
            return true;
        }

        private static void TryUnload(IPreviewHandler previewHandler)
        {
            try
            {
                previewHandler.Unload();
            }
            catch
            {
            }
        }

        private static void ReleaseComObject(object? comObject)
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }

        private enum PreviewInitializationMode
        {
            File,
            Stream,
        }
    }

    [ComImport]
    [Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref NativeRect rect);

        void SetRect(ref NativeRect rect);

        void DoPreview();

        void Unload();

        void SetFocus();

        void QueryFocus(out IntPtr phwnd);

        void TranslateAccelerator(ref MSG msg);
    }

    [ComImport]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithStream
    {
        void Initialize(IStream stream, uint grfMode);
    }

    [ComImport]
    [Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string filePath, uint grfMode);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentHandle,
            IntPtr menuHandle,
            IntPtr instanceHandle,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateStreamOnFileEx(
            string filePath,
            uint grfMode,
            uint attributes,
            [MarshalAs(UnmanagedType.Bool)] bool create,
            IStream? template,
            out IStream stream);
    }
}