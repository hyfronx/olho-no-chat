using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using OlhoNoChat.Sistema;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Janelas.LoginDoTikTok;

/// <summary>
/// A página de login do TikTok numa janela do app (o TikTok não tem um "Autorizar" como o da Twitch para ler o chat das
/// lives, então o login é aqui dentro), presa ao TikTok:
/// <list type="bullet">
/// <item>modo InPrivate: cookies e senha ficam só na memória, nada do login é gravado no disco pelo navegador;</item>
/// <item>só abre endereços HTTPS do TikTok (e do Google/Facebook/Apple, para "Continuar com…"); o endereço fica à vista;</item>
/// <item>sem ferramentas de desenvolvedor, sem salvar senha, sem preencher formulários, sem extensões, sem downloads;</item>
/// <item>quando o TikTok cria a sessão (cookie <c>sessionid</c>) e ela é confirmada, a janela fecha com
/// <see cref="Conta"/> e a pasta do navegador é apagada.</item>
/// </list>
/// As janelas que o login abre (o "Continuar com Google", por exemplo) são outras instâncias desta, no mesmo perfil.
/// </summary>
public partial class JanelaLoginDoTikTok : Window
{
    private readonly bool _ehPopup;
    private readonly string _pastaDoNavegador = "";
    private CoreWebView2Environment? _ambiente;
    private CoreWebView2ControllerOptions? _opcoes;
    private readonly CoreWebView2NewWindowRequestedEventArgs? _pedidoDoPopup;
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _conferindo;
    private readonly List<JanelaLoginDoTikTok> _popups = [];

    /// <summary>A conta que entrou, quando o login deu certo.</summary>
    public SessaoDoTikTok? Conta { get; private set; }

    public JanelaLoginDoTikTok()
    {
        InitializeComponent();
        _pastaDoNavegador = Path.Combine(InfoDoApp.PastaDoCofre, "login-" + Guid.NewGuid().ToString("N"));
        Loaded += async (_, _) =>
        {
            MolduraDaJanela.Aplicar(this);
            await AbrirAsync();
        };
        Closed += async (_, _) => await FecharAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    // Uma janela que o login abriu (mesmo navegador e perfil)
    private JanelaLoginDoTikTok(CoreWebView2Environment ambiente, CoreWebView2ControllerOptions opcoes, CoreWebView2NewWindowRequestedEventArgs pedido)
    {
        InitializeComponent();
        _ehPopup = true;
        _ambiente = ambiente;
        _opcoes = opcoes;
        _pedidoDoPopup = pedido;
        Width = 480;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Loaded += async (_, _) =>
        {
            MolduraDaJanela.Aplicar(this);
            await AbrirPopupAsync();
        };
    }

    /// <summary>Apaga as pastas de navegador que sobraram de um login interrompido (o app fechado no meio).</summary>
    public static void ApagarSobras()
    {
        try
        {
            if (!Directory.Exists(InfoDoApp.PastaDoCofre))
                return;
            foreach (string pasta in Directory.GetDirectories(InfoDoApp.PastaDoCofre, "login-*"))
                Directory.Delete(pasta, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("TikTok: uma pasta de login antiga não saiu: " + e.Message);
        }
    }

    private async Task AbrirAsync()
    {
        try
        {
            PastaProtegida.Garantir(InfoDoApp.PastaDoCofre);
            var opcoesDoAmbiente = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false };
            _ambiente = await CoreWebView2Environment.CreateAsync(null, _pastaDoNavegador, opcoesDoAmbiente);
            _opcoes = _ambiente.CreateCoreWebView2ControllerOptions();
            _opcoes.IsInPrivateModeEnabled = true;
            await navegador.EnsureCoreWebView2Async(_ambiente, _opcoes);
            Preparar(navegador.CoreWebView2);
            navegador.CoreWebView2.Navigate("https://www.tiktok.com/login");
            _relogio.Tick += async (_, _) => await ConferirSessaoAsync();
            _relogio.Start();
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or InvalidOperationException
                                      or IOException or ArgumentException or UnauthorizedAccessException)
        {
            MostrarAviso("O navegador do Windows (WebView2) não abriu: " + e.Message);
        }
    }

    private async Task AbrirPopupAsync()
    {
        CoreWebView2Deferral adiamento = _pedidoDoPopup!.GetDeferral();
        try
        {
            await navegador.EnsureCoreWebView2Async(_ambiente, _opcoes);
            Preparar(navegador.CoreWebView2);
            _pedidoDoPopup.NewWindow = navegador.CoreWebView2;
            _pedidoDoPopup.Handled = true;
        }
        finally
        {
            adiamento.Complete();
        }
    }

    private void Preparar(CoreWebView2 web)
    {
        CoreWebView2Settings s = web.Settings;
        s.AreDevToolsEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;

        web.NavigationStarting += (_, e) =>
        {
            if (EnderecosDoLogin.Permitido(e.Uri) || EnderecosDoLogin.Interno(e.Uri))
            {
                MostrarEndereco(e.Uri);
                return;
            }
            // Nunca abre no navegador da pessoa: no meio de um login seria uma página solta, sem o resto do caminho
            e.Cancel = true;
            Barrado(e.Uri, abrirFora: false);
        };
        // Quadros dentro da página (o captcha, por exemplo): só HTTPS
        web.FrameNavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !EnderecosDoLogin.Interno(e.Uri))
                e.Cancel = true;
        };
        web.NewWindowRequested += (_, e) =>
        {
            if (!EnderecosDoLogin.Permitido(e.Uri))
            {
                e.Handled = true;
                // Um link clicado que abre em outra janela (os Termos de Serviço, por exemplo): no navegador da pessoa
                Barrado(e.Uri, abrirFora: e.IsUserInitiated);
                return;
            }
            var popup = new JanelaLoginDoTikTok(_ambiente!, _opcoes!, e) { Owner = this };
            _popups.Add(popup);
            popup.Closed += (_, _) => _popups.Remove(popup);
            popup.Show();
        };
        web.WindowCloseRequested += (_, _) => Close();
        web.SourceChanged += (_, _) => MostrarEndereco(web.Source);
        web.NavigationCompleted += (_, _) => textoCarregando.Visibility = Visibility.Collapsed;
        web.DownloadStarting += (_, e) => e.Cancel = true;
    }

    // Um endereço fora do TikTok: só um link aberto em outra janela vai para o navegador da pessoa; o resto é barrado,
    // dizendo qual (para saber o que faltou, se um login parar no meio)
    private void Barrado(string endereco, bool abrirFora)
    {
        bool https = Uri.TryCreate(endereco, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
        string host = uri?.Host ?? "(endereço inválido)";
        if (abrirFora && https)
        {
            AbrirNoWindows.Site(uri!.AbsoluteUri);
            MostrarAviso($"O link para {host} abriu no seu navegador: esta janela só abre o TikTok.");
        }
        else
        {
            MostrarAviso($"Endereço barrado (esta janela só abre o TikTok e o login dele): {host}");
        }
        Debug.WriteLine("TikTok: login: endereço barrado: " + host);
    }

    private void MostrarEndereco(string endereco)
    {
        bool seguro = endereco.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        textoDoEndereco.Text = Uri.TryCreate(endereco, UriKind.Absolute, out Uri? uri) ? uri.Scheme + "://" + uri.Host + uri.AbsolutePath : endereco;
        iconeDoEndereco.Text = seguro ? "" : "";
        iconeDoEndereco.Foreground = new SolidColorBrush(seguro ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xE5, 0x53, 0x4B));
    }

    private void MostrarAviso(string texto)
    {
        textoDoAviso.Text = texto;
        textoDoAviso.Visibility = Visibility.Visible;
        textoCarregando.Visibility = Visibility.Collapsed;
    }

    // De segundo em segundo: quando o TikTok cria o cookie da sessão, confere com o TikTok e fecha
    private async Task ConferirSessaoAsync()
    {
        if (_conferindo || navegador.CoreWebView2 == null || Conta != null)
            return;
        _conferindo = true;
        try
        {
            List<CoreWebView2Cookie> cookies = await navegador.CoreWebView2.CookieManager.GetCookiesAsync("https://www.tiktok.com");
            string? sessao = cookies.FirstOrDefault(c => c.Name == "sessionid" && c.Value.Length > 0)?.Value;
            if (sessao == null)
                return;
            var (situacao, login, id) = await ConexaoComOTikTok.ConferirAsync(sessao, CancellationToken.None);
            if (situacao != SituacaoDaSessao.Valida)
                return;
            Conta = new SessaoDoTikTok(sessao, login, id);
            _relogio.Stop();
            DialogResult = true;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // O navegador fechando
        }
        finally
        {
            _conferindo = false;
        }
    }

    private void BotaoFecharJanela_Click(object sender, RoutedEventArgs e) => Close();

    // Fecha o navegador e apaga a pasta dele (espera o processo do navegador terminar para soltar os arquivos)
    private async Task FecharAsync()
    {
        _relogio.Stop();
        foreach (JanelaLoginDoTikTok popup in _popups.ToList())
            popup.Close();
        if (_ehPopup)
            return;
        var terminou = new TaskCompletionSource();
        if (_ambiente != null)
            _ambiente.BrowserProcessExited += (_, _) => terminou.TrySetResult();
        navegador.Dispose();
        await Task.WhenAny(terminou.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        for (int tentativa = 0; tentativa < 10 && Directory.Exists(_pastaDoNavegador); tentativa++)
        {
            try
            {
                Directory.Delete(_pastaDoNavegador, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
        if (Directory.Exists(_pastaDoNavegador))
            Debug.WriteLine("TikTok: a pasta do navegador do login não saiu agora; sai na próxima vez que o app abrir.");
    }
}
