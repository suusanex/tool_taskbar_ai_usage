using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarAiUsage;

/// <summary>Prevents Windows Shell from offering this window as a pin target.</summary>
internal static class WindowPinningPolicy
{
    private static readonly Guid PropertyStoreInterfaceId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey PreventPinningKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 9);

    internal static bool TryPreventPinning(IntPtr hwnd) => TryPreventPinning(hwnd, SetPreventPinning);

    internal static bool TryPreventPinning(IntPtr hwnd, Action<IntPtr> setPreventPinning)
    {
        ArgumentNullException.ThrowIfNull(setPreventPinning);
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            setPreventPinning(hwnd);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not set the window pinning policy: {exception.Message}");
            return false;
        }
    }

    private static void SetPreventPinning(IntPtr hwnd)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            var interfaceId = PropertyStoreInterfaceId;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref interfaceId, out propertyStore));
            var value = PropVariant.True;
            var key = PreventPinningKey;
            Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(propertyStore.Commit());
        }
        finally
        {
            if (propertyStore is not null && Marshal.IsComObject(propertyStore))
                Marshal.ReleaseComObject(propertyStore);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public short BooleanValue;

        public static PropVariant True => new() { VariantType = 11, BooleanValue = -1 };
    }
}
