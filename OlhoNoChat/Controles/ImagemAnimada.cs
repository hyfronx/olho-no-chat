using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OlhoNoChat.Controles;

/// <summary>
/// Liga uma <see cref="QuadrosDeGif"/> a uma Image: a animação roda só enquanto a imagem está na tela e, dentro de uma
/// rolagem, só enquanto aparece nela (numa lista que fechou, ou rolada para longe, a imagem mostra o que o Source dela
/// diz, normalmente o primeiro quadro). Assim uma lista com muitos emotes animados gasta pouco.
/// </summary>
public static class ImagemAnimada
{
    public static readonly DependencyProperty QuadrosProperty = DependencyProperty.RegisterAttached(
        "Quadros", typeof(QuadrosDeGif), typeof(ImagemAnimada), new PropertyMetadata(null, QuadrosMudaram));

    public static QuadrosDeGif? GetQuadros(DependencyObject elemento) => (QuadrosDeGif?)elemento.GetValue(QuadrosProperty);
    public static void SetQuadros(DependencyObject elemento, QuadrosDeGif? valor) => elemento.SetValue(QuadrosProperty, valor);

    // A rolagem em volta (enquanto a imagem está carregada), o que foi ligado nela e se a animação está rodando
    private static readonly DependencyProperty RolagemProperty = DependencyProperty.RegisterAttached(
        "Rolagem", typeof(ScrollViewer), typeof(ImagemAnimada));
    private static readonly DependencyProperty AoRolarProperty = DependencyProperty.RegisterAttached(
        "AoRolar", typeof(ScrollChangedEventHandler), typeof(ImagemAnimada));
    private static readonly DependencyProperty AnimandoProperty = DependencyProperty.RegisterAttached(
        "Animando", typeof(QuadrosDeGif), typeof(ImagemAnimada));

    private static void QuadrosMudaram(DependencyObject elemento, DependencyPropertyChangedEventArgs e)
    {
        if (elemento is not Image imagem)
            return;
        imagem.IsVisibleChanged -= VisivelMudou;
        imagem.Loaded -= Carregada;
        imagem.Unloaded -= Descarregada;
        if (e.NewValue != null)
        {
            imagem.IsVisibleChanged += VisivelMudou;
            imagem.Loaded += Carregada;
            imagem.Unloaded += Descarregada;
            if (imagem.IsLoaded)
                LigarNaRolagem(imagem);
        }
        else
        {
            SoltarDaRolagem(imagem);
        }
        Atualizar(imagem);
    }

    private static void VisivelMudou(object sender, DependencyPropertyChangedEventArgs e) => Atualizar((Image)sender);

    private static void Carregada(object sender, RoutedEventArgs e)
    {
        var imagem = (Image)sender;
        LigarNaRolagem(imagem);
        Atualizar(imagem);
    }

    private static void Descarregada(object sender, RoutedEventArgs e)
    {
        var imagem = (Image)sender;
        SoltarDaRolagem(imagem);
        Atualizar(imagem);
    }

    private static void LigarNaRolagem(Image imagem)
    {
        SoltarDaRolagem(imagem);
        ScrollViewer? rolagem = null;
        for (DependencyObject? pai = VisualTreeHelper.GetParent(imagem); pai != null && rolagem == null; pai = VisualTreeHelper.GetParent(pai))
            rolagem = pai as ScrollViewer;
        if (rolagem == null)
            return;
        ScrollChangedEventHandler aoRolar = (_, _) => Atualizar(imagem);
        rolagem.ScrollChanged += aoRolar;
        imagem.SetValue(RolagemProperty, rolagem);
        imagem.SetValue(AoRolarProperty, aoRolar);
    }

    private static void SoltarDaRolagem(Image imagem)
    {
        if (imagem.GetValue(RolagemProperty) is ScrollViewer rolagem && imagem.GetValue(AoRolarProperty) is ScrollChangedEventHandler aoRolar)
            rolagem.ScrollChanged -= aoRolar;
        imagem.ClearValue(RolagemProperty);
        imagem.ClearValue(AoRolarProperty);
    }

    // Só mexe na animação quando ela deve começar ou parar (começar de novo a cada rolagem faria o emote pular)
    private static void Atualizar(Image imagem)
    {
        QuadrosDeGif? quadros = GetQuadros(imagem);
        QuadrosDeGif? deve = quadros != null && imagem.IsVisible && ApareceNaRolagem(imagem) ? quadros : null;
        if (ReferenceEquals(deve, imagem.GetValue(AnimandoProperty)))
            return;
        imagem.SetValue(AnimandoProperty, deve);
        // null tira a animação: volta o valor do Source
        imagem.BeginAnimation(Image.SourceProperty, deve?.Animacao);
    }

    private static bool ApareceNaRolagem(Image imagem)
    {
        if (imagem.GetValue(RolagemProperty) is not ScrollViewer rolagem || !imagem.IsDescendantOf(rolagem))
            return true;
        Rect onde = imagem.TransformToAncestor(rolagem).TransformBounds(new Rect(imagem.RenderSize));
        return onde.IntersectsWith(new Rect(0, 0, rolagem.ActualWidth, rolagem.ActualHeight));
    }
}
