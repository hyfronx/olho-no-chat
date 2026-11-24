using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using OlhoNoChat.Controles;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A janela Configurações: abre no centro do monitor do chat, na aba Chat, com as páginas montadas de novo a cada
/// abertura. A lógica (e o que é salvo) fica em <see cref="LogicaConfiguracoes"/>.
/// </summary>
public partial class JanelaConfiguracoes : JanelaComMoldura
{
    public JanelaConfiguracoes(LogicaConfiguracoes logica, Window? chat)
    {
        Logica = logica;
        DataContext = logica;
        InitializeComponent();

        WindowStartupLocation = chat == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        // Ao ser criada e de novo ao aparecer: num monitor com outra escala do Windows, a janela muda de tamanho logo
        // depois de criada (ela nasce na escala do monitor principal), e ficaria fora do centro
        void Centralizar()
        {
            if (chat != null)
                JanelaDoWindows.CentralizarNoMonitorDe(new WindowInteropHelper(this).Handle, new WindowInteropHelper(chat).Handle);
        }
        SourceInitialized += (_, _) => Centralizar();
        Loaded += (_, _) => Centralizar();

        logica.Geral.RestaurarPedido += RestaurarTudo;
        logica.Twitch.TerminouDeConectar += () => Activate();
    }

    public LogicaConfiguracoes Logica { get; }

    protected override bool TemMudancas => Logica.TemMudancas;

    protected override void Salvar() => Logica.Salvar();

    private void ListaDePaginas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Chamado também enquanto a janela é montada, antes das páginas existirem
        if (paginas == null)
            return;

        for (int i = 0; i < paginas.Children.Count; i++)
            paginas.Children[i].Visibility = i == listaDePaginas.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Abre na aba Overlay, no endereço (o "Resolver" do balão da porta ocupada).</summary>
    public void MostrarOverlay()
    {
        listaDePaginas.SelectedItem = listaDePaginas.Items.OfType<ListBoxItem>()
            .First(item => AutomationProperties.GetName(item) == "Overlay");
        Dispatcher.BeginInvoke(paginaOverlay.MostrarEndereco, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void RestaurarTudo()
    {
        RespostaDoDialogo resposta = await PerguntarAsync("Restaurar tudo para o padrão?",
            "Todas as opções voltam como na primeira instalação, inclusive o que você mudou e ainda não salvou. " +
            "Ficam como estão: os canais, a conta da Twitch, a lista de usuários e as pessoas bloqueadas dos filtros, as palavras proibidas e o endereço do overlay.",
            "Restaurar", null, "Cancelar", RespostaDoDialogo.Fechar);
        if (resposta != RespostaDoDialogo.Principal)
            return;

        Logica.Restaurar();
        MostrarSalvo();
    }
}
