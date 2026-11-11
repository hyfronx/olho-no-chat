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

    /// <summary>
    /// Mostra o estado, com o "Encontrar outro endereço", e o endereço logo abaixo (o balão "Resolver" da porta ocupada
    /// abre a aba aqui), na parte Geral.
    /// </summary>
    public void MostrarEndereco()
    {
        listaDeAbas.SelectedIndex = 0;
        textoEstado.BringIntoView();
    }

    // As abas do topo: só o painel da escolhida aparece (a ordem dos painéis é a das abas)
    private void ListaDeAbas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Chamado também enquanto a página é montada, antes dos painéis existirem
        if (paineisDasAbas == null)
            return;
        for (int i = 0; i < paineisDasAbas.Children.Count; i++)
            paineisDasAbas.Children[i].Visibility = i == listaDeAbas.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        if (Logica != null)
            Copiar(Logica.CaminhoDoArquivo, textoCopiar);
    }

    private void CopiarEndereco_Click(object sender, RoutedEventArgs e)
    {
        if (Logica != null)
            Copiar(Logica.Endereco, textoCopiarEndereco);
    }

    private void Copiar(string texto, TextBlock textoDoBotao)
    {
        try
        {
            Clipboard.SetText(texto);
        }
        catch (COMException)
        {
            return; // outro programa está com a área de transferência aberta
        }
        // "Copiado" por um instante, no próprio botão (só um dos dois de cada vez)
        _voltarOCopiar?.Stop();
        textoCopiar.Text = "Copiar";
        textoCopiarEndereco.Text = "Copiar";
        textoDoBotao.Text = "Copiado";
        _voltarOCopiar = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _voltarOCopiar.Tick += (_, _) =>
        {
            _voltarOCopiar?.Stop();
            textoDoBotao.Text = "Copiar";
        };
        _voltarOCopiar.Start();
    }

    // "Mudar atalho"/"Confirmar": ver EditorDeAtalho.AlternarPeloBotao
    private void MudarAtalho_Click(object sender, RoutedEventArgs e) => EditorDeAtalho.AlternarPeloBotao(sender);

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (Logica != null)
            AbrirNoWindows.Site(Logica.Endereco);
    }
}
