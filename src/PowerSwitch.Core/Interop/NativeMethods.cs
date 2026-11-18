using System.Runtime.InteropServices;
using System.Text;

namespace PowerSwitch.Core.Interop;

internal static class NativeMethods
{
    internal const uint MapVkToScanCodeEx = 4;
    internal const uint MapScanCodeToVkEx = 3;
    internal const uint ToUnicodeNoKeyboardStateChange = 0x0004;
    internal const byte VkShift = 0x10;
    internal const byte VkControl = 0x11;
    internal const byte VkMenu = 0x12;
    internal const byte VkRightMenu = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetKeyboardLayoutList(
        int bufferLength,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[]? layouts);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadKeyboardLayout(string keyboardLayoutId, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern short VkKeyScanEx(char character, IntPtr keyboardLayout);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        [In] byte[] keyboardState,
        [Out] StringBuilder translatedCharacters,
        int bufferLength,
        uint flags,
        IntPtr keyboardLayout);
}
