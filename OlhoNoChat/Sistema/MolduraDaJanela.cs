using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static OlhoNoChat.Sistema.FuncoesDoWindows;

namespace OlhoNoChat.Sistema;

/// <summary>
/// A moldura das janelas com fundo (Configurações, Filtros do chat, Nova versão), igual em todas: o Windows 11 desenha
/// uma linha cinza fina em volta das janelas comuns, e ela ganha a cor da borda da própria janela (a sombra continua).
/// </summary>
internal static class MolduraDaJanela
{
    /// <summary>Chamar quando a janela carregar (a janela do Windows já existe).</summary>
    public static void Aplicar(Window janela)
    {
        // A janela transparente do chat desenha os próprios cantos: qualquer um destes atributos faz o Windows
        // desenhar a linha dele ali
        if (janela.AllowsTransparency)
            return;

        // Só existem no Windows 11 (o Windows 10 recusa os atributos e nada muda): cantos arredondados como os da
        // janela do chat e a linha em volta na cor da borda da própria janela, para ela não aparecer
        IntPtr hwnd = new WindowInteropHelper(janela).Handle;
        uint arredondado = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref arredondado, sizeof(uint));
        if (janela.Background is SolidColorBrush { Color: var borda })
        {
            uint cor = borda.R | ((uint)borda.G << 8) | ((uint)borda.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref cor, sizeof(uint));
        }
    }
}
