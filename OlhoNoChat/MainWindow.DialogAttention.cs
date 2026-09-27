namespace OlhoNoChat;

using System.Windows.Interop;
using OlhoNoChat.Helpers;

/// <summary>
/// While Configurações (or Filtros do chat) is open, Windows keeps the chat disabled. A click on the
/// chat used to do nothing; now it brings the open window to the front and blinks its orange frame.
/// With hidden borders the chat lets clicks through to the game, so this never happens there.
/// </summary>
public partial class MainWindow
{
    private const int WM_SETCURSOR = 0x0020;
    private const int HTERROR = -2;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

    private void StartDialogAttention()
    {
        this.SourceInitialized += (s, e) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(DialogAttentionHook);
    }

    // A disabled window gets WM_SETCURSOR with HTERROR when it is clicked (also over the chat page)
    private IntPtr DialogAttentionHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_SETCURSOR)
            return IntPtr.Zero;

        long value = lParam.ToInt64();
        int hitTest = (short)(value & 0xFFFF);
        int mouseMessage = (int)((value >> 16) & 0xFFFF);

        if (hitTest == HTERROR && mouseMessage is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
            && WindowAttention.CallAttentionToOpenDialog(hwnd))
        {
            // Handled here: no Windows error beep
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }
}
