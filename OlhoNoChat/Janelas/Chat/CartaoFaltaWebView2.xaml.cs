#nullable enable
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OlhoNoChat.Chat;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// "Falta um componente": sem o WebView2 no Windows, o chat não aparece. "Baixar e instalar" baixa o instalador pequeno da
/// Microsoft (que baixa o resto), roda e espera; depois confere a cada 2,5 s se o WebView2 já existe e avisa a janela,
/// que carrega o chat sem reiniciar o app.
/// </summary>
public partial class CartaoFaltaWebView2 : UserControl
{
    /// <summary>O instalador pequeno da Microsoft.</summary>
    public const string EnderecoDoInstalador = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private DispatcherTimer? _conferir;

    public CartaoFaltaWebView2()
    {
        InitializeComponent();
    }

    /// <summary>O WebView2 apareceu no Windows.</summary>
    public event Action? Instalado;

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        botaoInstalarWebView2.IsEnabled = false;
        botaoInstalarWebView2.Content = "Baixando...";
        try
        {
            string pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat");
            Directory.CreateDirectory(pasta);
            string instalador = Path.Combine(pasta, "MicrosoftEdgeWebview2Setup.exe");

            // O arquivo inteiro (uns 2 MB) dentro do tempo limite: uma conexão parada não deixa o botão preso
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                await File.WriteAllBytesAsync(instalador, await http.GetByteArrayAsync(EnderecoDoInstalador));

            botaoInstalarWebView2.Content = "Instalando...";
            using (Process? processo = Process.Start(new ProcessStartInfo(instalador) { UseShellExecute = true }))
            {
                if (processo != null)
                    await processo.WaitForExitAsync();
            }

            // Mesmo se a pessoa cancelou o instalador: o chat só aparece quando o WebView2 existir de verdade
            botaoInstalarWebView2.Content = "Instalação concluída";
            textoFaltaWebView2.Text = "WebView2 instalado com sucesso! O app vai recarregar sozinho.";
            ConferirAteAparecer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível baixar ou instalar o WebView2. Confira sua internet e tente de novo.\n\n{ex.Message}",
                "Falha na instalação");
            botaoInstalarWebView2.IsEnabled = true;
            botaoInstalarWebView2.Content = "Baixar e instalar";
        }
    }

    private void ConferirAteAparecer()
    {
        if (_conferir != null)
            return;

        _conferir = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _conferir.Tick += (_, _) =>
        {
            if (!NavegadorDoChat.EstaInstalado())
                return;
            _conferir.Stop();
            _conferir = null;
            Instalado?.Invoke();
        };
        _conferir.Start();
    }
}
