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

    internal static void Apply(IntPtr handle, bool dark)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero) return;
        try
        {
            int on = dark ? 1 : 0;
            int size = sizeof(int);
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, size) != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref on, size);

            // DWM COLORREF: 0x00BBGGRR, dark neutral matching the tab strip.
            int caption = dark ? 0x00202020 : 0x00F2F2F2;
            int foreground = dark ? 0x00F2F2F2 : 0x00202020;
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, size);
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref foreground, size);
            int rounded = 2;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, size);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}


public sealed partial class BrowserForm
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private void ToggleMaximized()
    {
        if (WindowState != FormWindowState.Maximized)
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private void DragCaption(int clicks)
    {
        if (clicks > 1) { ToggleMaximized(); return; }
        ReleaseCapture();
        SendWindowMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x83)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
        if (message.Msg != 0x84 || WindowState == FormWindowState.Maximized || message.Result != (IntPtr)1)
            return;

        long coordinates = message.LParam.ToInt64();
        var point = PointToClient(new Point((short)coordinates, (short)(coordinates >> 16)));
        int edge = Math.Max(5, DeviceDpi * 5 / 96);
        bool left = point.X < edge, right = point.X >= ClientSize.Width - edge;
        bool top = point.Y < edge, bottom = point.Y >= ClientSize.Height - edge;
        if (top) message.Result = (IntPtr)(left ? 13 : right ? 14 : 12);
        else if (bottom) message.Result = (IntPtr)(left ? 16 : right ? 17 : 15);
        else if (left || right) message.Result = (IntPtr)(left ? 10 : 11);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep the sizing frame for Windows resize behavior; remove it from layout below.
            parameters.Style |= 0x00040000 | 0x00080000 | 0x00030000;
            return parameters;
        }
    }
}
