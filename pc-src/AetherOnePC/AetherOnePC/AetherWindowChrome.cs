using System;
using System.Runtime.InteropServices;

namespace AetherOnePC;

/// <summary>
/// Native Windows 10/11 dark title bar integration. Keeps OS window controls,
/// accessibility and snapping while matching the dark browser shell.
/// </summary>
internal static class AetherWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    internal static void Apply(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero) return;
        try
        {
            int on = 1;
            int size = sizeof(int);
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, size) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref on, size);

            // DWM COLORREF: 0x00BBGGRR, dark neutral matching the tab strip.
            int caption = 0x00202020;
            int foreground = 0x00F2F2F2;
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, size);
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref foreground, size);
            int rounded = 2;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, size);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
