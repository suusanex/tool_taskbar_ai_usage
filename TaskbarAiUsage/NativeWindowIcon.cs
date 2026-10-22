using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace TaskbarAiUsage;

internal interface IWindowIconPlatform
{
    IntPtr CreateIcon(BitmapSource bitmap);
    void SetWindowIcon(IntPtr hwnd, IntPtr iconType, IntPtr icon);
    void DestroyIcon(IntPtr icon);
}

internal sealed class NativeWindowIcon : IDisposable
{
    private readonly IntPtr hwnd;
    private readonly IWindowIconPlatform platform;
    private IntPtr currentIcon;
    private bool disposed;

    internal NativeWindowIcon(IntPtr hwnd, IWindowIconPlatform? platform = null)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("A valid window handle is required.", nameof(hwnd));
        this.hwnd = hwnd;
        this.platform = platform ?? Win32WindowIconPlatform.Instance;
    }

    internal IntPtr CurrentIcon => currentIcon;

    internal void Apply(BitmapSource bitmap)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var replacement = platform.CreateIcon(bitmap);
        if (replacement == IntPtr.Zero)
            throw new InvalidOperationException("Windows could not create the taskbar icon.");

        try
        {
            platform.SetWindowIcon(hwnd, Win32WindowIconPlatform.IconBig, replacement);
            platform.SetWindowIcon(hwnd, Win32WindowIconPlatform.IconSmall, replacement);
        }
        catch
        {
            platform.DestroyIcon(replacement);
            throw;
        }

        var previous = currentIcon;
        currentIcon = replacement;
        if (previous != IntPtr.Zero)
            platform.DestroyIcon(previous);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (currentIcon == IntPtr.Zero) return;
        platform.SetWindowIcon(hwnd, Win32WindowIconPlatform.IconBig, IntPtr.Zero);
        platform.SetWindowIcon(hwnd, Win32WindowIconPlatform.IconSmall, IntPtr.Zero);
        platform.DestroyIcon(currentIcon);
        currentIcon = IntPtr.Zero;
    }
}

internal sealed class Win32WindowIconPlatform : IWindowIconPlatform
{
    internal static readonly Win32WindowIconPlatform Instance = new();
    internal static readonly IntPtr IconSmall = new(0);
    internal static readonly IntPtr IconBig = new(1);

    public IntPtr CreateIcon(BitmapSource bitmap)
    {
        var colorBitmap = CreateColorBitmap(bitmap);
        if (colorBitmap == IntPtr.Zero)
            throw new InvalidOperationException("Windows could not create the icon color bitmap.");
        var maskBitmap = CreateOpaqueMask(bitmap.PixelWidth, bitmap.PixelHeight);
        if (maskBitmap == IntPtr.Zero)
        {
            DeleteObject(colorBitmap);
            throw new InvalidOperationException("Windows could not create the icon mask bitmap.");
        }

        try
        {
            var info = new IconInfo { IsIcon = true, ColorBitmap = colorBitmap, MaskBitmap = maskBitmap };
            var icon = CreateIconIndirect(ref info);
            if (icon == IntPtr.Zero)
                throw new InvalidOperationException("Windows could not create an icon handle.");
            return icon;
        }
        finally
        {
            DeleteObject(colorBitmap);
            DeleteObject(maskBitmap);
        }
    }

    private static IntPtr CreateColorBitmap(BitmapSource bitmap)
    {
        const int bytesPerPixel = 4;
        var stride = checked(bitmap.PixelWidth * bytesPerPixel);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bitmap.PixelWidth,
                Height = -bitmap.PixelHeight,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = checked((uint)(stride * bitmap.PixelHeight))
            }
        };
        var handle = CreateDIBSection(IntPtr.Zero, ref info, 0, out var pixels, IntPtr.Zero, 0);
        if (handle != IntPtr.Zero)
            bitmap.CopyPixels(new System.Windows.Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), pixels, checked(stride * bitmap.PixelHeight), stride);
        return handle;
    }

    private static IntPtr CreateOpaqueMask(int width, int height)
    {
        var bytesPerRow = checked(((width + 15) / 16) * 2);
        var zeroBits = new byte[checked(bytesPerRow * height)];
        var pin = GCHandle.Alloc(zeroBits, GCHandleType.Pinned);
        try { return CreateBitmap(width, height, 1, 1, pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
    }

    public void SetWindowIcon(IntPtr hwnd, IntPtr iconType, IntPtr icon) => SendMessage(hwnd, 0x0080, iconType, icon);
    public void DestroyIcon(IntPtr icon) => DestroyIconNative(icon);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [DllImport("user32.dll", EntryPoint = "CreateIconIndirect", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
    private static extern bool DestroyIconNative(IntPtr icon);

    [DllImport("gdi32.dll", EntryPoint = "CreateBitmap", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);
}

internal static class TaskbarIconMessageRouter
{
    internal const int WmDpiChanged = 0x02E0;

    internal static bool Handle(int message, int taskbarButtonCreated, int taskbarCreated, Action reapply)
    {
        if (message != WmDpiChanged && message != taskbarButtonCreated && message != taskbarCreated)
            return false;
        reapply();
        return true;
    }
}

internal static class ShellWindowMessages
{
    internal static readonly int TaskbarButtonCreated = Register("TaskbarButtonCreated");
    internal static readonly int TaskbarCreated = Register("TaskbarCreated");

    internal static void EnsureRegistered() { }

    private static int Register(string name)
    {
        var id = RegisterWindowMessage(name);
        if (id == 0) throw new InvalidOperationException($"Could not register the {name} window message.");
        return unchecked((int)id);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
}
