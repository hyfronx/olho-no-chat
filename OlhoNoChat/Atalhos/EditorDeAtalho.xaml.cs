#nullable enable
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OlhoNoChat.Atalhos;

/// <summary>
/// A caixa de um atalho em Configurações > Geral. Mostra "Ctrl + Alt + F9" ou "Nenhum". Gravando, cada tecla
/// apertada (com os modificadores seguros) vira o atalho; Esc, Delete ou Backspace sozinhos apagam o atalho.
/// </summary>
public partial class EditorDeAtalho
{
    public static readonly DependencyProperty AtalhoProperty = DependencyProperty.Register(
        nameof(Atalho), typeof(Atalho), typeof(EditorDeAtalho),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (objeto, _) => ((EditorDeAtalho)objeto).MostrarAtalho()));

    // Teclas que sozinhas não são a tecla do atalho: o editor espera a tecla principal
    private static readonly HashSet<Key> SoModificadores = new()
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift,
        Key.LWin, Key.RWin, Key.Clear, Key.OemClear, Key.Apps
    };

    private static readonly HashSet<Key> TeclasQueApagam = new() { Key.Escape, Key.Delete, Key.Back };

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

    /// <summary>Esperando as teclas do atalho novo (depois de "Mudar atalho").</summary>
    public bool Gravando { get; private set; }

    public void ComecarAGravar()
    {
        Gravando = true;
        caixa.Focusable = true;
        caixa.Focus();
    }

    public void PararDeGravar()
    {
        Gravando = false;
        caixa.Focusable = false;
    }

    private void MostrarAtalho() => caixa.Text = Atalho.TextoOuNenhum(Atalho);

    private void Caixa_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Nenhuma tecla faz o que faria numa caixa de texto comum
        e.Handled = true;
        if (!Gravando)
            return;

        // Com Alt apertado o WPF manda a tecla em SystemKey
        Key tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modificadores = Keyboard.Modifiers;

        if (modificadores == ModifierKeys.None && TeclasQueApagam.Contains(tecla))
        {
            Atalho = null;
            return;
        }

        if (SoModificadores.Contains(tecla))
            return;

        Atalho = new Atalho(tecla, modificadores);
    }
}
