using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Chat;

/// <summary>
/// O navegador interno (WebView2) que mostra o chat na janela: um ambiente leve, a pasta "browser" servida como
/// https://olhonochat.example, o cache limpo antes de cada navegação, o zoom, o CSS e os scripts de cada página, as
/// mensagens das páginas, os links abertos no navegador padrão, a vigia do Padrão e a recuperação sem perguntar nada
/// quando um processo do navegador cai.
/// </summary>
public sealed class NavegadorDoChat
{
    /// <summary>Os limites do "Tamanho do texto" (o zoom da página).</summary>
    public const double ZoomMinimo = 0.5;
    public const double ZoomMaximo = 2.5;

    // O navegador ao lado do jogo gasta o mínimo: o som toca sem clique, os timers da página não ficam lentos em segundo
    // plano, e os serviços que um chat não usa ficam desligados
    private const string ArgumentosDoNavegador =
        "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --msWebView2CancelInitialNavigation" +
        " --disable-background-networking --disable-component-update --disable-extensions --disable-sync --no-first-run" +
        " --disable-features=Translate,msEdgeTranslate,OptimizationHints,AutofillServerCommunication,MediaRouter";

    private readonly ILogger _logger;
    private readonly Grid _lugar;
    private readonly int _linha;
    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _linksAbrem;
    private readonly VigiaDoChat _vigia = new();
    private readonly RecuperacaoDoNavegador _recuperacao = new();
    private readonly DispatcherTimer _timerDaVigia = new() { Interval = VigiaDoChat.Intervalo };
    private DispatcherTimer? _recargaAgendada;
    private Task<CoreWebView2Environment>? _ambiente;
    private bool _vigiaPerguntando;

    /// <param name="lugar">A grade da janela onde o navegador entra.</param>
    /// <param name="linha">A linha da grade.</param>
    /// <param name="opcoes">As opções em uso.</param>
    /// <param name="linksAbrem">Se um link clicado abre agora no navegador padrão (só com as bordas visíveis).</param>
    public NavegadorDoChat(ILogger logger, Grid lugar, int linha, Func<Opcoes> opcoes, Func<bool> linksAbrem)
    {
        _logger = logger;
        _lugar = lugar;
        _linha = linha;
        _opcoes = opcoes;
        _linksAbrem = linksAbrem;
        _timerDaVigia.Tick += async (_, _) => await VigiarAsync();
    }

    /// <summary>O controle na janela (null antes de <see cref="CriarAsync"/>). Muda quando o navegador é recriado.</summary>
    public WebView2? Controle { get; private set; }

    /// <summary>A página aberta por último (null antes da primeira).</summary>
    public PaginaDoChat? Pagina { get; private set; }

    /// <summary>Alguma página já carregou e nenhuma outra está carregando.</summary>
    public bool PaginaPronta => _vigia.PaginaPronta;

    /// <summary>O controle entrou na janela (também depois de recriado), antes de o navegador começar.</summary>
    public event Action<WebView2>? ControleCriado;

    /// <summary>Uma navegação do chat começou (o que estava na página vai embora).</summary>
    public event Action? ComecouACarregar;

    /// <summary>A página terminou de carregar (true), já com o CSS e os scripts, ou falhou (false).</summary>
    public event Action<bool>? Carregou;

    public event Action<MensagemDaPagina>? Mensagem;

    /// <summary>O navegador não pôde ser recuperado: o texto para mostrar antes de o app fechar.</summary>
    public event Action<string>? FalhouDeVez;

    /// <summary>Se o WebView2 está instalado no Windows.</summary>
    public static bool EstaInstalado()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Cria o navegador na janela. A primeira página vem depois, por <see cref="AbrirDasOpcoes"/>.</summary>
    public async Task CriarAsync()
    {
        await CriarControleAsync(anterior: null);
        _timerDaVigia.Start();
    }

    private async Task CriarControleAsync(WebView2? anterior)
    {
        var controle = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        Grid.SetRow(controle, _linha);
        if (anterior != null)
        {
            // O novo fica como o antigo estava: as bordas, o modo rolagem e o foco no jogo continuam
            Grid.SetRowSpan(controle, Grid.GetRowSpan(anterior));
            controle.Margin = anterior.Margin;
            controle.Focusable = anterior.Focusable;
        }
        _lugar.Children.Add(controle);
        Controle = controle;
        ControleCriado?.Invoke(controle);

        _ambiente ??= CoreWebView2Environment.CreateAsync(null, InfoDoApp.PastaDeDados,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = ArgumentosDoNavegador });
        await controle.EnsureCoreWebView2Async(await _ambiente);

        CoreWebView2 navegador = controle.CoreWebView2;
        navegador.SetVirtualHostNameToFolderMapping(InfoDoApp.HostDasPaginas, InfoDoApp.PastaDasPaginas,
            CoreWebView2HostResourceAccessKind.DenyCors);
        try
        {
            navegador.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não deu para pedir pouca memória ao navegador interno.");
        }

        controle.NavigationCompleted += async (_, e) => await TerminouDeCarregarAsync(e.IsSuccess, e.WebErrorStatus);
        navegador.WebMessageReceived += (_, e) => Mensagem?.Invoke(ContratoComAPagina.LerMensagem(TextoDaMensagem(e)));
        navegador.NewWindowRequested += NovaJanelaPedida;
        navegador.ProcessFailed += async (_, e) => await ProcessoFalhouAsync(e);
    }

    private static string? TextoDaMensagem(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            return e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return null; // não é texto
        }
    }

    // --- Páginas ---------------------------------------------------------------------------------------------

    /// <summary>Abre a página das opções salvas (o chat do canal, o endereço ou as boas-vindas).</summary>
    public void AbrirDasOpcoes() => Abrir(PaginaDoChat.DasOpcoes(_opcoes()));

    /// <summary>Abre uma página; abrir a mesma de novo recarrega (apaga as mensagens e o cache).</summary>
    public async void Abrir(PaginaDoChat pagina)
    {
        if (Controle?.CoreWebView2 == null)
            return;

        Pagina = pagina;
        try
        {
            AntesDeNavegar();
            // Uma entrada estragada no cache em disco já quebrou a página (um script falhando ao ser lido)
            await LimparCacheAsync();
            Controle.CoreWebView2.Navigate(pagina.Endereco);
        }
        catch (Exception ex)
        {
            _vigia.NaoCarregou();
            string endereco = string.IsNullOrEmpty(pagina.Endereco) ? "<Empty>" : pagina.Endereco;
            _logger.LogError(ex, "Não deu para abrir o endereço do chat: {Endereco}", endereco);
            MessageBox.Show($"Não foi possível abrir esse endereço.\nErro: {ex.Message}\nEndereço: '{endereco}'", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AntesDeNavegar()
    {
        _recargaAgendada?.Stop(); // a página nova substitui a página de erro
        _vigia.ComecouACarregar();
        ComecouACarregar?.Invoke();
    }

    private async Task LimparCacheAsync()
    {
        try
        {
            CoreWebView2Profile? perfil = Controle?.CoreWebView2?.Profile;
            if (perfil != null)
                await perfil.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não deu para limpar o cache do navegador interno.");
        }
    }

    // Zoom, extensões (chat oficial), CSS + script numa chamada só; a página de boas-vindas não recebe nada
    private async Task TerminouDeCarregarAsync(bool sucesso, CoreWebView2WebErrorStatus erro)
    {
        _vigia.TerminouDeCarregar();
        _recuperacao.PaginaCarregou();

        if (!sucesso)
        {
            _logger.LogWarning("A página do chat não carregou: {Erro}", erro);
            Carregou?.Invoke(false);
            return;
        }

        Opcoes opcoes = _opcoes();
        opcoes.TamanhoDoTexto = AplicarZoom(opcoes.TamanhoDoTexto);

        if (Pagina != null)
        {
            try
            {
                foreach (string script in Pagina.ScriptsAntesDoCss(opcoes))
                    await Controle!.CoreWebView2.ExecuteScriptAsync(script);

                string? aoCarregar = Pagina.ScriptAoCarregar(opcoes);
                if (aoCarregar != null)
                    await Controle!.CoreWebView2.ExecuteScriptAsync(aoCarregar);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não deu para preparar a página do chat.");
            }
        }

        Carregou?.Invoke(true);
    }

    /// <summary>
    /// Depois de "Salvar" (Configurações ou Filtros do chat): recarrega só quando algo que a página lê ao abrir mudou
    /// (a <see cref="PaginaDoChat.ChaveDeRecarga"/>); o resto vale ao vivo, sem perder as mensagens da tela.
    /// </summary>
    public async Task AplicarOpcoesSalvasAsync()
    {
        Opcoes opcoes = _opcoes();
        PaginaDoChat nova = PaginaDoChat.DasOpcoes(opcoes);
        if (Pagina == null || nova.ChaveDeRecarga != Pagina.ChaveDeRecarga)
        {
            Abrir(nova);
            return;
        }

        string? script = Pagina.ScriptAoVivo(opcoes);
        if (script == null || Controle?.CoreWebView2 == null)
            return;

        if (!Pagina.RecarregaSeNaoAplicar)
        {
            // Durante uma navegação não: a página nova já vai receber as opções novas
            if (!_vigia.Carregando)
                Executar(script);
            return;
        }

        bool aplicou = false;
        if (!_vigia.Carregando)
        {
            try
            {
                aplicou = await Controle.CoreWebView2.ExecuteScriptAsync(script) == "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não deu para aplicar as opções na página aberta; o chat vai recarregar.");
            }
        }
        if (!aplicou)
            Abrir(nova);
    }

    /// <summary>Aplica o "Tamanho do texto" (dentro dos limites, arredondado) e diz o valor que ficou.</summary>
    public double AplicarZoom(double zoom)
    {
        zoom = Math.Round(Math.Clamp(zoom, ZoomMinimo, ZoomMaximo), 2);
        if (Controle != null)
            Controle.ZoomFactor = zoom;
        return zoom;
    }

    /// <summary>Roda um script na página aberta, sem esperar a resposta.</summary>
    public async void Executar(string script)
    {
        if (Controle?.CoreWebView2 == null)
            return;
        try
        {
            await Controle.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Script na página do chat falhou.");
        }
    }

    // --- Links -----------------------------------------------------------------------------------------------

    // Nenhuma janela de navegador dentro do app: um link com target=_blank abre no navegador padrão, e só com as bordas
    // visíveis. As páginas de login da Twitch abrem dentro do app (a caixa da própria Twitch precisa do login ali).
    private void NovaJanelaPedida(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (EhLoginDaTwitch(e.Uri))
            return;

        e.Handled = true;
        if (_linksAbrem() && Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? endereco)
            && (endereco.Scheme == Uri.UriSchemeHttps || endereco.Scheme == Uri.UriSchemeHttp))
        {
            AbrirNoWindows.Site(endereco.AbsoluteUri);
        }
    }

    /// <summary>id.twitch.tv, passport.twitch.tv e twitch.tv/login ou /signup.</summary>
    public static bool EhLoginDaTwitch(string? endereco)
    {
        if (!Uri.TryCreate(endereco, UriKind.Absolute, out Uri? uri))
            return false;

        string site = uri.Host.ToLowerInvariant();
        string caminho = uri.AbsolutePath.ToLowerInvariant();
        return site is "id.twitch.tv" or "passport.twitch.tv"
               || (site is "www.twitch.tv" or "twitch.tv" && (caminho.StartsWith("/login") || caminho.StartsWith("/signup")));
    }

    // --- Vigia e recuperação ---------------------------------------------------------------------------------

    private async Task VigiarAsync()
    {
        if (_vigiaPerguntando || Pagina is not ChatPadrao || Controle?.CoreWebView2 == null || !_vigia.DevePerguntar())
            return;

        _vigiaPerguntando = true;
        try
        {
            SaudeDaPagina saude = ContratoComAPagina.LerSaude(
                await Controle.CoreWebView2.ExecuteScriptAsync(ContratoComAPagina.PerguntarSaude));
            string? motivo = _vigia.Avaliar(saude);
            if (motivo != null)
            {
                _logger.LogWarning("Vigia do chat: {Motivo}. Abrindo o chat de novo.", motivo);
                Abrir(Pagina);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A vigia do chat não conseguiu perguntar à página.");
        }
        finally
        {
            _vigiaPerguntando = false;
        }
    }

    // O WebView2 reinicia sozinho os processos de placa de vídeo e outros auxiliares: só o processo do navegador e o da
    // página precisam do app. Nada é perguntado (nenhuma janela por cima do jogo).
    private async Task ProcessoFalhouAsync(CoreWebView2ProcessFailedEventArgs e)
    {
        _logger.LogWarning("Processo do navegador interno falhou: {Tipo}, motivo {Motivo}, código {Codigo}.",
            e.ProcessFailedKind, e.Reason, e.ExitCode);

        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                await RecriarAsync();
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                RecarregarDepoisDeFalha(agendarSeNaoPuder: true);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // O aviso se repete enquanto a página continua travada: nada é agendado
                if (_recuperacao.TravadaHaMuitoTempo())
                    RecarregarDepoisDeFalha(agendarSeNaoPuder: false);
                break;
        }
    }

    private async Task RecriarAsync()
    {
        _recargaAgendada?.Stop(); // o navegador novo carrega o chat

        // Tirado na hora: o controle antigo dá erro em qualquer uso (um atalho, o tamanho da janela mudando...)
        WebView2? anterior = Controle;
        if (anterior != null)
        {
            _lugar.Children.Remove(anterior);
            anterior.Dispose();
        }
        Controle = null;
        _ambiente = null; // o ambiente antigo morreu com o processo

        if (!_recuperacao.PodeRecriarONavegador())
        {
            _timerDaVigia.Stop();
            await Task.Yield(); // nenhuma caixa de mensagem dentro do evento do WebView2
            FalhouDeVez?.Invoke("Erro grave: o navegador interno parou várias vezes seguidas. O app será fechado.");
            return;
        }

        try
        {
            await CriarControleAsync(anterior);
            AbrirDasOpcoes();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não deu para recriar o navegador interno.");
            _timerDaVigia.Stop();
            FalhouDeVez?.Invoke($"Erro grave: não foi possível recuperar o navegador interno. {ex.Message} O app será fechado.");
        }
    }

    // Um processo novo da página começou numa página de erro: o chat carrega de novo. Passado o limite, e se pedido, a
    // recarga fica para quando outra for permitida.
    private void RecarregarDepoisDeFalha(bool agendarSeNaoPuder)
    {
        if (!_recuperacao.PodeRecarregarAPagina(out TimeSpan tentarEm))
        {
            _logger.LogWarning("Falhas demais da página do chat: não recarrega agora.");
            if (agendarSeNaoPuder)
                AgendarRecarga(tentarEm);
            return;
        }

        try
        {
            AntesDeNavegar();
            Controle?.CoreWebView2.Reload();
        }
        catch (Exception ex)
        {
            _vigia.NaoCarregou();
            _logger.LogWarning(ex, "Não deu para recarregar o chat depois de uma falha do navegador interno.");
        }
    }

    // Cancelada por qualquer navegação (AntesDeNavegar)
    private void AgendarRecarga(TimeSpan daquiA)
    {
        if (_recargaAgendada == null)
        {
            _recargaAgendada = new DispatcherTimer();
            _recargaAgendada.Tick += (_, _) =>
            {
                _recargaAgendada.Stop();
                RecarregarDepoisDeFalha(agendarSeNaoPuder: true);
            };
        }
        _recargaAgendada.Interval = daquiA + TimeSpan.FromSeconds(1);
        _recargaAgendada.Start();
    }
}
