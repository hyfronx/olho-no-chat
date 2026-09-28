#nullable enable
using System.Windows;
using System.Windows.Interop;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.NovaVersao;

/// <summary>
/// "Nova versão disponível": "Atualizar agora" fecha com true; "Depois", Esc e "×" fecham sem atualizar. Abre no centro
/// do monitor onde está o chat.
/// </summary>
public partial class JanelaNovaVersao : Window
{
    public JanelaNovaVersao(LogicaNovaVersao logica, Window? chat)
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
        Loaded += (_, _) => MolduraDaJanela.Aplicar(this);
    }

    public LogicaNovaVersao Logica { get; }

    private void BotaoAtualizar_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BotaoFecharJanela_Click(object sender, RoutedEventArgs e) => Close();
}
