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
        EscalaDaInterface.Acompanhar(this);

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
        Loaded += (_, _) => MolduraDaJanela.Aplicar(this);
    }

    public LogicaNovaVersao Logica { get; }

    private void BotaoAtualizar_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BotaoFecharJanela_Click(object sender, RoutedEventArgs e) => Close();
}
