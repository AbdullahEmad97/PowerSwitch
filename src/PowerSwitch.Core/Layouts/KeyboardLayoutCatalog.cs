using PowerSwitch.Core.Interop;

namespace PowerSwitch.Core.Layouts;

public static class KeyboardLayoutCatalog
{
    public static IReadOnlyList<KeyboardLayoutInfo> GetInstalledLayouts()
    {
        EnsureWindows();

        var count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count < 0)
        {
            throw new InvalidOperationException("Windows could not enumerate keyboard layouts.");
        }

        if (count == 0)
        {
            return Array.Empty<KeyboardLayoutInfo>();
        }

        var handles = new IntPtr[count];
        var retrieved = NativeMethods.GetKeyboardLayoutList(handles.Length, handles);
        if (retrieved < 0)
        {
            throw new InvalidOperationException("Windows could not retrieve installed keyboard layouts.");
        }

        return handles
            .Take(retrieved)
            .Select(KeyboardLayoutInfo.FromHandle)
            .ToArray();
    }

    public static KeyboardLayoutInfo? TryLoadLayout(string keyboardLayoutId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyboardLayoutId);
        if (keyboardLayoutId.Length != 8 || !keyboardLayoutId.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A keyboard layout ID must contain exactly eight hexadecimal characters.", nameof(keyboardLayoutId));
        }

        EnsureWindows();

        var handle = NativeMethods.LoadKeyboardLayout(keyboardLayoutId, 0);
        return handle == IntPtr.Zero ? null : KeyboardLayoutInfo.FromHandle(handle);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Keyboard layout enumeration is available only on Windows.");
        }
    }
}
