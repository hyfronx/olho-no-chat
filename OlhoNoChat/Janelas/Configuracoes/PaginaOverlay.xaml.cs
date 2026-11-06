using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "Overlay" de Configurações (DataContext = <see cref="LogicaOverlay"/>).</summary>
public partial class PaginaOverlay : UserControl
{
    private DispatcherTimer? _voltarOCopiar;

    public PaginaOverlay()
    {
        InitializeComponent();
    }

    private LogicaOverlay? Logica => DataContext as LogicaOverlay;

    /// <summary>Mostra o cartão do endereço (o balão "Resolver" da porta ocupada abre a aba aqui).</summary>
    public void MostrarEndereco() => caixaEndereco.BringIntoView();

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        if (Logica == null)
            return;
        try
        {
            Clipboard.SetText(Logica.Endereco);
        }
        catch (COMException)
        {
            return; // outro programa está com a área de transferência aberta
        }
        // "Copiado" por um instante, no próprio botão
        textoCopiar.Text = "Copiado";
        _voltarOCopiar?.Stop();
        _voltarOCopiar = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _voltarOCopiar.Tick += (_, _) =>
        {
            _voltarOCopiar?.Stop();
            textoCopiar.Text = "Copiar";
        };
        _voltarOCopiar.Start();
    }

    // "Mudar atalho" começa a gravar na caixa; "Confirmar" para (igual à aba Geral). Vale depois de "Salvar".
    private void MudarAtalho_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditorDeAtalho editor } botao)
            return;

        if (editor.Gravando)
        {
            editor.PararDeGravar();
            botao.Content = "Mudar atalho";
        }
        else
        {
            editor.ComecarAGravar();
            botao.Content = "Confirmar";
        }
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (Logica != null)
            AbrirNoWindows.Site(Logica.Endereco);
    }
}
