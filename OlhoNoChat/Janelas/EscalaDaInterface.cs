using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;

namespace OlhoNoChat.Janelas;

/// <summary>
/// O "Tamanho da interface" (Configurações > Aparência): encolhe ou aumenta junto tudo o que é do app e não é o texto
/// das mensagens (esse tem o seu, na barra laranja): a barra laranja, a faixa do canal, a caixa de escrever, os menus,
/// os painéis, as dicas e as janelas de Configurações, Filtros e Nova versão.
/// <para>
/// O Windows só aumenta (100%, 125%...), nunca diminui: numa tela de resolução baixa os mesmos pixels ocupam um pedaço
/// bem maior da tela e tudo parece grande. No automático, o app escolhe pelo tamanho da tela onde o chat está, e
/// escolhe de novo quando o chat muda de monitor.
/// </para>
/// <para>
/// A escala em uso fica nos recursos do app, com a chave <see cref="Chave"/>: o XAML a usa como
/// <c>LayoutTransform="{DynamicResource EscalaDaInterface}"</c> e acompanha sozinho quando ela muda.
/// </para>
/// </summary>
public static class EscalaDaInterface
{
    public const string Chave = "EscalaDaInterface";

    /// <summary>O valor gravado em <see cref="Configuracoes.Opcoes.TamanhoDaInterface"/> para "o app escolhe".</summary>
    public const double Automatica = 0;

    public const double Minima = 0.7;
    public const double Maxima = 1.3;
    public const double Passo = 0.05;

    /// <summary>O automático nunca encolhe mais que isto (menor, as letras ficam difíceis de ler) nem aumenta.</summary>
    public const double MinimaAutomatica = 0.75;

    // A tela para a qual a interface foi desenhada: Full HD. Dela para cima, 100%
    private const double AreaDeReferencia = 1920.0 * 1080.0;

    /// <summary>A escala em uso agora (1 = 100%).</summary>
    public static double Atual { get; private set; } = 1;

    public static event Action? Mudou;

    /// <summary>
    /// A escala que o automático escolhe para uma tela (tamanho em pixels já divididos pela escala do Windows): na
    /// proporção do lado da tela em relação ao Full HD (pela área, para valer igual em telas largas e quadradas),
    /// de 5 em 5%. 1280 × 960 e 1366 × 768 dão 75%; 1600 × 900, 85%; Full HD ou mais, 100%.
    /// </summary>
    public static double Automatico(double largura, double altura)
    {
        if (!(largura > 0) || !(altura > 0))
            return 1;
        double proporcao = Math.Sqrt(largura * altura / AreaDeReferencia);
        return Math.Clamp(Arredondar(proporcao), MinimaAutomatica, 1);
    }

    /// <summary>A escala para o valor gravado: o automático (0 ou um valor sem sentido) ou o escolhido, nos limites.</summary>
    public static double Calcular(double opcao, double largura, double altura) =>
        opcao > 0 ? Math.Clamp(Arredondar(opcao), Minima, Maxima) : Automatico(largura, altura);

    private static double Arredondar(double escala) => Math.Round(Math.Round(escala / Passo) * Passo, 2);

    /// <summary>Passa a usar a escala (o XAML acompanha pelos recursos do app). Devolve se ela mudou.</summary>
    public static bool Definir(double escala)
    {
        if (Math.Abs(escala - Atual) < 0.001)
            return false;

        Atual = escala;
        if (Application.Current is Application app)
        {
            var transformacao = new ScaleTransform(escala, escala);
            transformacao.Freeze();
            app.Resources[Chave] = transformacao;
        }
        Mudou?.Invoke();
        return true;
    }

    /// <summary>
    /// Para as janelas com tamanho próprio (Configurações, Filtros, Nova versão), cujo conteúdo já segue a escala pelo
    /// XAML: o tamanho, o tamanho mínimo e a altura da parte que arrasta a janela acompanham a escala, ao abrir e
    /// quando ela muda com a janela aberta. Chamar no construtor, antes de quem centraliza a janela.
    /// </summary>
    public static void Acompanhar(Window janela)
    {
        double aplicada = 1;
        double? alturaDeArrastar = null;

        void Ajustar()
        {
            double fator = Atual / aplicada;
            aplicada = Atual;

            if (WindowChrome.GetWindowChrome(janela) is WindowChrome moldura)
            {
                alturaDeArrastar ??= moldura.CaptionHeight;
                var nova = (WindowChrome)moldura.Clone();
                nova.CaptionHeight = alturaDeArrastar.Value * Atual;
                WindowChrome.SetWindowChrome(janela, nova);
            }

            if (Math.Abs(fator - 1) < 0.001)
                return;
            janela.MinWidth *= fator;
            janela.MinHeight *= fator;
            if (!double.IsNaN(janela.Width))
                janela.Width *= fator;
            if (!double.IsNaN(janela.Height) && janela.SizeToContent == SizeToContent.Manual)
                janela.Height *= fator;
        }

        janela.SourceInitialized += (_, _) => Ajustar();
        Mudou += Ajustar;
        janela.Closed += (_, _) => Mudou -= Ajustar;
    }
}
