#nullable enable
using System.Runtime.InteropServices;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Todas as chamadas diretas ao Windows (user32, gdi32, dwmapi, kernel32) num lugar só. O resto do app usa as
/// classes desta pasta, que dão nomes claros a essas chamadas.
/// </summary>
internal static partial class FuncoesDoWindows
{
    // Estilo estendido da janela
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20;
    public const long WS_EX_TOPMOST = 0x08;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr janela, int indice);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr janela, int indice, IntPtr valor);

    // Foco e ordem das janelas
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr janela);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr janela, IntPtr depoisDe, int x, int y, int largura, int altura, uint opcoes);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr janela, out uint processo);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr janela);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowEnabled(IntPtr janela);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr janela);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr janela, int comando);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassName(IntPtr janela, [Out] char[] nome, int tamanho);

    // Chamadas que recebem uma função de volta usam DllImport: o LibraryImport não converte delegates
    public delegate bool AoListarJanela(IntPtr janela, IntPtr parametro);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(AoListarJanela aoListar, IntPtr parametro);

    public delegate void AoMudarJanela(IntPtr gancho, uint evento, IntPtr janela, int objeto, int filho, uint thread, uint hora);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventoMinimo, uint eventoMaximo, IntPtr modulo, AoMudarJanela aoMudar, uint processo, uint thread, uint opcoes);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr gancho);

    // Piscar a janela na barra de tarefas
    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    public const uint FLASHW_ALL = 0x0003;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlashWindowEx(ref FLASHWINFO info);

    // Moldura do Windows 11
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const uint DWMWCP_ROUND = 2;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr janela, int atributo, ref uint valor, int tamanho);

    // Atalhos globais
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr janela, int id, uint modificadores, uint tecla);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr janela, int id);

    // Recorte de uma janela (canto de redimensionar do chat)
    public const int RGN_DIFF = 4;

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRectRgn(int esquerda, int topo, int direita, int baixo);

    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(IntPtr destino, IntPtr origem1, IntPtr origem2, int modo);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr objeto);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(IntPtr janela, IntPtr regiao, [MarshalAs(UnmanagedType.Bool)] bool redesenhar);

    // Posição das janelas e área útil do monitor (sem a barra de tarefas)
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint SWP_NOZORDER = 0x0004;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr janela, out RECT retangulo);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr janela, uint opcoes);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    // Teclado
    public const uint KEYEVENTF_KEYUP = 0x2;

    [LibraryImport("user32.dll")]
    public static partial void keybd_event(byte tecla, byte scan, uint opcoes, UIntPtr extra);
}
