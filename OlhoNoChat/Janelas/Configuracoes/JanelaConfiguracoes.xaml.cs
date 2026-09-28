#nullable enable
using System.Windows;
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
        SourceInitialized += (_, _) =>
        {
            if (chat != null)
                JanelaDoWindows.CentralizarNoMonitorDe(new WindowInteropHelper(this).Handle, new WindowInteropHelper(chat).Handle);
        };

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

    private async void RestaurarTudo()
    {
        RespostaDoDialogo resposta = await PerguntarAsync("Restaurar tudo para o padrão?",
            "Todas as opções voltam como na primeira instalação (também o que você mudou e ainda não salvou). " +
            "O canal, a conta da Twitch e as listas de nomes dos filtros continuam.",
            "Restaurar", null, "Cancelar", RespostaDoDialogo.Fechar);
        if (resposta != RespostaDoDialogo.Principal)
            return;

        Logica.Restaurar();
        MostrarSalvo();
    }
}
