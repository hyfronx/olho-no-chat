#nullable enable
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static OlhoNoChat.Sistema.FuncoesDoWindows;
using Color = System.Windows.Media.Color;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Enquanto uma janela como Configurações está aberta, o Windows deixa o chat desativado: um clique no chat não
/// faz nada. Isto acha a janela aberta, traz para a frente e pisca a moldura laranja dela.
/// </summary>
internal static class ChamarAtencao
{
    private static readonly Color LaranjaDaMoldura = Color.FromRgb(0xE8, 0x5D, 0x30);
    private static readonly Color CorDaPiscada = Color.FromRgb(0xFF, 0xE4, 0xD8);
    private static readonly TimeSpan DuracaoDaPiscada = TimeSpan.FromMilliseconds(340);
    private const int Piscadas = 3;
    private const string MarcaDoContorno = "ContornoDaPiscada";
    private const string ClasseDaCaixaDeMensagem = "#32770";

    /// <summary>
    /// Chama a atenção para a janela que mantém <paramref name="janelaDesativada"/> desativada. Devolve false se
    /// não houver nenhuma.
    /// </summary>
    public static bool ParaAJanelaAberta(IntPtr janelaDesativada, Type tipoDaJanelaDoChat)
    {
        IntPtr aberta = AcharJanelaAberta(janelaDesativada, tipoDaJanelaDoChat);
        if (aberta == IntPtr.Zero)
            return false;

        if (IsIconic(aberta))
            ShowWindow(aberta, SW_RESTORE);

        // Acima das janelas "sempre na frente", e depois o foco, se o Windows deixar
        JanelaDoWindows.ColocarNaFrente(aberta, ativar: true);
        JanelaDoWindows.DarFoco(aberta);

        if (HwndSource.FromHwnd(aberta)?.RootVisual is Window janela && PiscarMoldura(janela))
            return true;

        // Outras janelas (uma caixa de mensagem, por exemplo): a piscada normal do Windows
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = aberta,
            dwFlags = FLASHW_ALL,
            uCount = Piscadas,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
        return true;
    }

    // A janela do app mais à frente que está visível e ativa (uma janela WPF ou uma caixa de mensagem)
    private static IntPtr AcharJanelaAberta(IntPtr janelaDesativada, Type tipoDaJanelaDoChat)
    {
        uint thread = GetCurrentThreadId();
        IntPtr achada = IntPtr.Zero;

        // O Windows lista da janela mais à frente para a de trás
        EnumWindows((hwnd, parametro) =>
        {
            if (hwnd == janelaDesativada || GetWindowThreadProcessId(hwnd, out _) != thread
                || !IsWindowVisible(hwnd) || !IsWindowEnabled(hwnd))
                return true;

            // Pula dicas, menus e a própria janela do chat
            bool ehJanela = HwndSource.FromHwnd(hwnd)?.RootVisual is Window janela
                ? !tipoDaJanelaDoChat.IsInstanceOfType(janela)
                : JanelaDoWindows.ClasseDaJanela(hwnd) == ClasseDaCaixaDeMensagem;

            if (!ehJanela)
                return true;

            achada = hwnd;
            return false;
        }, IntPtr.Zero);

        return achada;
    }

    // Pisca a moldura laranja (o "Moldura" do modelo das janelas com moldura: Configurações e Filtros do chat) e um
    // contorno claro em volta da janela inteira. Devolve false para janelas sem essa moldura.
    private static bool PiscarMoldura(Window janela)
    {
        Grid? moldura = janela.Template?.FindName("Moldura", janela) as Grid;
        if (moldura?.Background is not SolidColorBrush pincel
            || (Color)pincel.GetAnimationBaseValue(SolidColorBrush.ColorProperty) != LaranjaDaMoldura)
            return false;

        if (pincel.IsFrozen)
        {
            pincel = pincel.Clone();
            moldura.Background = pincel;
        }

        // Criado na primeira piscada e mantido (invisível) depois; clicar de novo durante a piscada recomeça
        var contorno = moldura.Children.OfType<Border>().FirstOrDefault(b => MarcaDoContorno.Equals(b.Tag));
        if (contorno == null)
        {
            contorno = new Border
            {
                Tag = MarcaDoContorno,
                BorderBrush = new SolidColorBrush(CorDaPiscada),
                BorderThickness = new Thickness(3),
                IsHitTestVisible = false,
                Opacity = 0
            };
            Grid.SetRowSpan(contorno, Math.Max(1, moldura.RowDefinitions.Count));
            Grid.SetColumnSpan(contorno, Math.Max(1, moldura.ColumnDefinitions.Count));
            Panel.SetZIndex(contorno, int.MaxValue);
            moldura.Children.Add(contorno);
        }

        var corDaMoldura = (Color)pincel.GetAnimationBaseValue(SolidColorBrush.ColorProperty);
        var animacaoDaCor = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var animacaoDoContorno = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };

        for (int i = 0; i < Piscadas; i++)
        {
            TimeSpan inicio = TimeSpan.FromTicks(DuracaoDaPiscada.Ticks * i);
            TimeSpan acesa = inicio + TimeSpan.FromMilliseconds(70);
            TimeSpan apagando = inicio + TimeSpan.FromMilliseconds(170);
            TimeSpan fim = inicio + DuracaoDaPiscada;

            animacaoDaCor.KeyFrames.Add(new LinearColorKeyFrame(corDaMoldura, KeyTime.FromTimeSpan(inicio)));
            animacaoDaCor.KeyFrames.Add(new LinearColorKeyFrame(CorDaPiscada, KeyTime.FromTimeSpan(acesa)));
            animacaoDaCor.KeyFrames.Add(new LinearColorKeyFrame(CorDaPiscada, KeyTime.FromTimeSpan(apagando)));
            animacaoDaCor.KeyFrames.Add(new LinearColorKeyFrame(corDaMoldura, KeyTime.FromTimeSpan(fim)));

            animacaoDoContorno.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(inicio)));
            animacaoDoContorno.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(acesa)));
            animacaoDoContorno.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(apagando)));
            animacaoDoContorno.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(fim)));
        }

        pincel.BeginAnimation(SolidColorBrush.ColorProperty, animacaoDaCor);
        contorno.BeginAnimation(UIElement.OpacityProperty, animacaoDoContorno);
        return true;
    }
}
