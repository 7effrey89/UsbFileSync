using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace UsbFileSync.App.Services;

public static class PreviewCursorFactory
{
    public static System.Windows.Input.Cursor CreateZoomInCursor() => CreateZoomCursor(drawPlus: true);

    public static System.Windows.Input.Cursor CreateZoomOutCursor() => CreateZoomCursor(drawPlus: false);

    private static System.Windows.Input.Cursor CreateZoomCursor(bool drawPlus)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var ringPen = new Pen(Color.FromArgb(32, 32, 32), 2.5f);
            using var highlightPen = new Pen(Color.FromArgb(255, 255, 255), 1.3f);

            graphics.DrawEllipse(ringPen, 4, 4, 16, 16);
            graphics.DrawLine(ringPen, 17, 17, 26, 26);
            graphics.DrawEllipse(highlightPen, 5, 5, 14, 14);

            using var symbolPen = new Pen(Color.FromArgb(15, 108, 189), 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            graphics.DrawLine(symbolPen, 8, 12, 16, 12);
            if (drawPlus)
            {
                graphics.DrawLine(symbolPen, 12, 8, 12, 16);
            }
        }

        var colorBitmapHandle = bitmap.GetHbitmap();
        var maskBitmapHandle = new Bitmap(32, 32, PixelFormat.Format1bppIndexed).GetHbitmap();

        try
        {
            var iconInfo = new IconInfo
            {
                IsIcon = false,
                XHotspot = 8,
                YHotspot = 8,
                MaskBitmap = maskBitmapHandle,
                ColorBitmap = colorBitmapHandle,
            };

            var cursorHandle = CreateIconIndirect(ref iconInfo);
            if (cursorHandle == IntPtr.Zero)
            {
                return System.Windows.Input.Cursors.Hand;
            }

            return CursorInteropHelper.Create(new SafeCursorHandle(cursorHandle));
        }
        finally
        {
            DeleteObject(colorBitmapHandle);
            DeleteObject(maskBitmapHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;

        public int XHotspot;

        public int YHotspot;

        public IntPtr MaskBitmap;

        public IntPtr ColorBitmap;
    }

    private sealed class SafeCursorHandle : SafeHandle
    {
        public SafeCursorHandle(IntPtr preexistingHandle)
            : base(IntPtr.Zero, true)
        {
            SetHandle(preexistingHandle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => DestroyCursor(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(IntPtr cursorHandle);
}