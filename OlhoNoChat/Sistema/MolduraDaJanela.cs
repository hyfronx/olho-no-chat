#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static OlhoNoChat.Sistema.FuncoesDoWindows;

namespace OlhoNoChat.Sistema;

/// <summary>
/// A moldura de toda janela do app, igual em todas. O ModernWpf desenha os botões de minimizar, maximizar e
/// fechar dele na barra de título: eles são escondidos, e cada janela desenha o próprio botão de fechar (estilo
/// TitleCloseButton em Styles/TitleBar.xaml). O Windows 11 também desenha uma linha cinza fina em volta das
/// janelas comuns: ela ganha a cor da borda da janela (a sombra continua).
/// </summary>
internal static class MolduraDaJanela
{
    private const string BarraDoModernWpf = "ModernWpf.Controls.Primitives.TitleBarControl";
    private static readonly string[] BotoesDoModernWpf = { "MinimizeButton", "PART_MaximizeRestoreButton", "CloseButton" };

    /// <summary>Chamar quando a janela carregar (o modelo visual e a janela do Windows já existem).</summary>
    public static void Aplicar(Window janela)
    {
        EsconderBotoesDoModernWpf(janela);

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

    private static void EsconderBotoesDoModernWpf(Window janela)
    {
        // Só a aparência da barra: se o modelo do ModernWpf mudar, a janela continua funcionando igual
        DependencyObject? barra = Procurar(janela, e => e.GetType().FullName == BarraDoModernWpf);
        if (barra == null)
            return;

        foreach (string nome in BotoesDoModernWpf)
        {
            if (Procurar(barra, e => e is Button b && b.Name == nome) is Button botao)
                botao.Visibility = Visibility.Collapsed;
        }
    }

    // Primeiro elemento da árvore visual (em largura) que atende à condição
    private static DependencyObject? Procurar(DependencyObject inicio, Func<DependencyObject, bool> condicao)
    {
        var fila = new Queue<DependencyObject>();
        fila.Enqueue(inicio);
        while (fila.Count > 0)
        {
            DependencyObject atual = fila.Dequeue();
            int filhos = VisualTreeHelper.GetChildrenCount(atual);
            for (int i = 0; i < filhos; i++)
            {
                DependencyObject filho = VisualTreeHelper.GetChild(atual, i);
                if (condicao(filho))
                    return filho;
                fila.Enqueue(filho);
            }
        }
        return null;
    }
}
