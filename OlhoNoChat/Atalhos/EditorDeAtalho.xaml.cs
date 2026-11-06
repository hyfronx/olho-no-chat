using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OlhoNoChat.Atalhos;

/// <summary>
/// A caixa de um atalho em Configurações (abas Geral e Overlay). Mostra "Ctrl + Alt + F9" ou "Nenhum" e, com atalho,
/// um "×" que tira o atalho. Gravando, cada tecla apertada (com os modificadores) vira o atalho; Delete ou Backspace
/// sozinhos tiram o atalho; Esc sozinho desiste: volta o atalho de antes de gravar e para de gravar.
/// </summary>
public partial class EditorDeAtalho
{
    public static readonly DependencyProperty AtalhoProperty = DependencyProperty.Register(
        nameof(Atalho), typeof(Atalho), typeof(EditorDeAtalho),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (objeto, _) => ((EditorDeAtalho)objeto).MostrarAtalho()));

    private static readonly DependencyPropertyKey GravandoPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Gravando), typeof(bool), typeof(EditorDeAtalho), new PropertyMetadata(false));

    public static readonly DependencyProperty GravandoProperty = GravandoPropertyKey.DependencyProperty;

    // Teclas que sozinhas não são a tecla do atalho: o editor espera a tecla principal
    private static readonly HashSet<Key> SoModificadores = new()
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift,
        Key.LWin, Key.RWin, Key.Clear, Key.OemClear, Key.Apps
    };

    private static readonly HashSet<Key> TeclasQueTiram = new() { Key.Delete, Key.Back };

    // O atalho de quando "Mudar atalho" foi clicado, para o Esc voltar a ele
    private Atalho? _antesDeGravar;

    public EditorDeAtalho()
    {
        InitializeComponent();
        MostrarAtalho();
    }

    public Atalho? Atalho
    {
        get => (Atalho?)GetValue(AtalhoProperty);
        set => SetValue(AtalhoProperty, value);
    }

    /// <summary>
    /// Esperando as teclas do atalho novo (depois de "Mudar atalho"). Propriedade de dependência para o botão da
    /// linha dizer "Confirmar" enquanto isso, também quando o Esc para a gravação.
    /// </summary>
    public bool Gravando
    {
        get => (bool)GetValue(GravandoProperty);
        private set => SetValue(GravandoPropertyKey, value);
    }

    /// <summary>
    /// O clique no botão "Mudar atalho"/"Confirmar" ao lado da caixa (no Tag dele): começa a gravar, ou para. O nome do
    /// botão segue a caixa pelo estilo BotaoMudarAtalho. O atalho só vale depois de "Salvar".
    /// </summary>
    public static void AlternarPeloBotao(object sender)
    {
        if (sender is not FrameworkElement { Tag: EditorDeAtalho editor })
            return;
        if (editor.Gravando)
            editor.PararDeGravar();
        else
            editor.ComecarAGravar();
    }

    public void ComecarAGravar()
    {
        _antesDeGravar = Atalho;
        Gravando = true;
        caixa.Focusable = true;
        caixa.Focus();
    }

    public void PararDeGravar()
    {
        Gravando = false;
        caixa.Focusable = false;
    }

    private void MostrarAtalho()
    {
        caixa.Text = Atalho.TextoOuNenhum(Atalho);
        botaoTirar.Visibility = Atalho.Existe(Atalho) ? Visibility.Visible : Visibility.Collapsed;
    }

    // O "×": gravando ou não, fica sem atalho (gravando, a caixa continua esperando as teclas até "Confirmar")
    private void Tirar_Click(object sender, RoutedEventArgs e) => Atalho = null;

    private void Caixa_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Nenhuma tecla faz o que faria numa caixa de texto comum
        e.Handled = true;
        if (!Gravando)
            return;

        // Com Alt apertado o WPF manda a tecla em SystemKey
        Key tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modificadores = Keyboard.Modifiers;

        if (modificadores == ModifierKeys.None && tecla == Key.Escape)
        {
            Desistir();
            return;
        }

        if (modificadores == ModifierKeys.None && TeclasQueTiram.Contains(tecla))
        {
            Atalho = null;
            return;
        }

        if (SoModificadores.Contains(tecla))
            return;

        Atalho = new Atalho(tecla, modificadores);
    }

    // Esc: o atalho volta a ser o de antes, a gravação para e o foco vai para o "Mudar atalho" da linha (o próximo do
    // Tab), para o teclado continuar andando pela janela
    private void Desistir()
    {
        Atalho = _antesDeGravar;
        PararDeGravar();
        caixa.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
}
