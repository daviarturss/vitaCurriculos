using System.Runtime.InteropServices;

namespace Vita.App;

/// <summary>
/// Minimal native dialog used only for fatal startup errors. The published app runs
/// without a console window, so this is the last-resort way to tell the user what happened.
/// </summary>
internal static class NativeDialog
{
    private const uint MbIconError = 0x00000010;
    private const uint MbOk = 0x00000000;
    private const uint MbSetForeground = 0x00010000;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void ShowError(string title, string message)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { MessageBox(IntPtr.Zero, message, title, MbIconError | MbOk | MbSetForeground); }
        catch { /* dialog is best-effort */ }
    }
}
