using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Inicio;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Kick;
using OlhoNoChat.Overlay;
using OlhoNoChat.Sistema;
using OlhoNoChat.Som;
using OlhoNoChat.Twitch;
using OlhoNoChat.Atalhos;
using OlhoNoChat.TikTok;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A janela do chat, por cima do jogo: liga as peças (barra laranja, faixa do canal, caixa de escrever, navegador do chat,
/// avisos, sempre na frente, canto de redimensionar, ícone perto do relógio, atalhos) e aplica o estado que
/// <see cref="LogicaJanelaChat"/> e as lógicas da faixa e da caixa decidem.
/// </summary>
public partial class JanelaChat : Window
{
    private const int WM_SETCURSOR = 0x0020;
    private const int HTERROR = -2;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

    private readonly ILogger<JanelaChat> _log;
    private readonly ArquivoDeConfiguracoes _arquivo;
    private readonly ContaDaTwitch _conta;
    private readonly AutorizacaoNoNavegador _autorizacao;
    private readonly ApiDaTwitch _api;
    private readonly EventSubDaTwitch _eventSub;
    private readonly AutoModDoCanal _autoMod;
    // Cada atualização do EventSub ganha um número: uma que terminou depois de outra mais nova não vale
    private int _versaoDoEventSub;
    private readonly LeitorDoYouTube _youTube;
    private readonly LeitorDaKick _kick;
    private readonly LeitorDoTikTok _tikTok;
    private readonly ContagemDeViewers _viewers;
    private readonly ContaDoTikTok _contaDoTikTok;
    private readonly ServidorDoOverlay _overlay;
    // O endereço do overlay ocupado: o balão (com as bordas) e o aviso na página (sem elas) ainda não apareceram. Um aviso
    // por problema: volta só depois que o overlay abrir (ou for desligado) e der problema de novo
    private bool _balaoDoOverlayPendente;
    private bool _avisoDoOverlayNaPaginaPendente;
    private bool _problemaDoOverlayAvisado;
    private const string AvisoDoOverlay = "aviso-do-overlay";
    private readonly ProcuraDeAtualizacoes _atualizacoes;
    private readonly IconeDaBandeja _bandeja;
    private readonly LogicaJanelaChat _logica = new();
    private readonly LogicaFaixaDoCanal _faixa;
    private readonly LogicaCaixaDeEscrever _caixa;
    private readonly NavegadorDoChat _navegador;
    private readonly AvisosNoChat _avisos;
    private readonly SempreNaFrente _sempreNaFrente;
    private readonly CantoDeRedimensionar _canto;
    private readonly AtalhosGlobais _atalhos;
    private readonly TocadorDeAviso _som = new();
    private JanelaConfiguracoes? _janelaDeConfiguracoes;
    // A pausa do chat Padrão como a página contou (ChatAoVivo, ChatPausado ou ChatContinuando)
    private MensagemDaPagina _pausa = MensagemDaPagina.ChatAoVivo;
    private DispatcherTimer? _esperaDaNovidade;
    // A novidade no balão agora (null = nenhuma)
    private string? _novidadeNaTela;
    private bool _temWebView2;
    private bool _fechando;

    public JanelaChat(ILogger<JanelaChat> log, ArquivoDeConfiguracoes arquivo, ContaDaTwitch conta, AutorizacaoNoNavegador autorizacao,
        EnvioDeMensagem envio, ListaDeEmotes listaDeEmotes, ImagensDeEmotes imagensDeEmotes, ApiDaTwitch api, EventSubDaTwitch eventSub,
        LeitorDoYouTube youTube, LeitorDaKick kick, LeitorDoTikTok tikTok, ContaDoTikTok contaDoTikTok, ProcuraDeAtualizacoes atualizacoes,
        IconeDaBandeja bandeja, ServidorDoOverlay overlay)
    {
        InitializeComponent();

        _log = log;
        _arquivo = arquivo;
        _conta = conta;
        _autorizacao = autorizacao;
        _api = api;
        _eventSub = eventSub;
        _autoMod = new AutoModDoCanal(api, conta, log);
        _youTube = youTube;
        _kick = kick;
        _tikTok = tikTok;
        _contaDoTikTok = contaDoTikTok;
        _viewers = new ContagemDeViewers(log, new FontesDeViewers(), () => youTube.LiveAtual);
        _overlay = overlay;
        _atualizacoes = atualizacoes;
        _bandeja = bandeja;
        _atalhos = new AtalhosGlobais(log);

        _faixa = new LogicaFaixaDoCanal(() => Opcoes, () => _conta.EstaConectada, _conta.CanalExisteAsync, TrocarCanal);
        faixaDoCanal.Logica = _faixa;
        _caixa = new LogicaCaixaDeEscrever(() => Opcoes, () => _conta.EstaConectada, () => _conta.PodeEnviar, () => _conta.NomeMostrado,
            envio.EnviarAsync);
        caixaDeEscrever.Logica = _caixa;
        caixaDeEscrever.Ligar(listaDeEmotes, imagensDeEmotes, conta, autorizacao, log);
        caixaDeEscrever.FecharPedido += () => FecharCaixa(devolverFoco: true);
        caixaDeEscrever.Enviada += () =>
        {
            if (_caixa.FechaDepoisDeEnviar)
                FecharCaixa(devolverFoco: true);
            else
                caixaDeEscrever.Focar(); // pronta para a próxima
        };

        _navegador = new NavegadorDoChat(log, grade, linha: 2, () => Opcoes, () => _logica.LinksClicaveis);
        _avisos = new AvisosNoChat(() => _navegador.PaginaPronta && _navegador.Controle?.CoreWebView2 != null, _navegador.Executar,
            arquivo.Aviso, () => Opcoes.MostrarDicasDeAjuda);
        _sempreNaFrente = new SempreNaFrente(this, () => !_fechando && _janelaDeConfiguracoes == null);
        _canto = new CantoDeRedimensionar(this, () => _logica.BordasVisiveis && WindowState == WindowState.Normal);
        cartaoFaltaWebView2.Instalado += () => _ = PrepararNavegadorAsync();

        LigarBarra();
        LigarBandeja();
        LigarNavegador();
        _eventSub.Resgatado += resgate => Dispatcher.InvokeAsync(() => MostrarResgate(resgate));
        LigarAutoMod();
        LigarYouTube();
        LigarKick();
        LigarTikTok();
        LigarViewers();
        LigarOverlay();
        _som.SaidaVoltouParaAPadrao += GravarSaidaDeSomPadrao;
        _conta.Mudou += () => Dispatcher.BeginInvoke(ContaMudou);
        // "Atualizar agora": o Velopack fecha o app sem passar pelo Closing, então a posição vai para as opções antes
        _atualizacoes.AntesDeReiniciar = () => Opcoes.Janela = PosicaoAtual();
        _atualizacoes.ProcuraAutomaticaDesligada += () => _janelaDeConfiguracoes?.Logica.ProcuraAutomaticaDesligada();
        _atualizacoes.EstadoMudou += () =>
        {
            if (_janelaDeConfiguracoes != null)
                _janelaDeConfiguracoes.Logica.Geral.Procura = _atualizacoes.Estado;
        };

        // Onde a janela estava (na primeira vez, o tamanho do XAML); nunca abre maximizada nem minimizada (decisão 18)
        Opcoes.Janela?.AplicarEm(this);
        WindowState = WindowState.Normal;
        SourceInitialized += (_, _) => AoCriarAJanela();
        Loaded += (_, _) =>
        {
            // Toda vez que o app abre
            if (Opcoes.ProcurarAtualizacoes)
                _ = _atualizacoes.ProcurarAsync(manual: false, dono: null);
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            // Um popup ficaria solto na tela com a janela minimizada
            if (WindowState == WindowState.Minimized)
                EsconderNovidade();
            else
                MostrarNovidadeSeFaltar();
        };
        _faixa.PropertyChanged += (_, e) =>
        {
            // O balão ficaria por cima das caixas da faixa aberta
            if (e.PropertyName != nameof(LogicaFaixaDoCanal.EditorAberto))
                return;
            if (_faixa.EditorAberto)
                EsconderNovidade();
            else
                MostrarNovidadeSeFaltar();
        };
        Deactivated += (_, _) =>
        {
            // Clicar no jogo (ou em qualquer outra janela) enquanto escreve pelo atalho fecha a caixa
            if (_caixa.PeloAtalho && !_caixa.DaTwitchPeloBotao)
                TerminarEscrita(devolverFoco: false);
        };
        Closing += (_, _) => GravarAoFechar();
        Closed += (_, _) =>
        {
            _atalhos.Dispose();
            _eventSub.Desligar();
            _youTube.Desligar();
            _kick.Desligar();
            _tikTok.Desligar();
            _viewers.Dispose();
            _overlay.Desligar();
            _som.Dispose();
            _sempreNaFrente.Dispose();
        };
        grade.SizeChanged += (_, _) => RecortarCantos();

        DefinirAtalhos();
        AplicarSempreNoTopo();
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
        barraDeCima.MostrarMultiplataforma(Opcoes.ChatMultiplataforma);
        AplicarEstado();
        _ = PrepararNavegadorAsync();
        AtualizarOverlay();
        _ = _conta.VerificarUmaVezAsync(); // o acesso salvo pode ter expirado ou sido removido
    }

    private Opcoes Opcoes => _arquivo.Opcoes;

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    // --- Abertura --------------------------------------------------------------------------------------------

    private void AoCriarAJanela()
    {
        IntPtr hwnd = Hwnd;
        HwndSource.FromHwnd(hwnd)?.AddHook(Mensagens);
        JanelaDoWindows.TirarMaximizar(hwnd);

        // Primeira vez: no centro do monitor principal; uma posição fora de todos os monitores volta para lá
        if (JanelaDoWindows.RetanguloDe(hwnd) is Retangulo agora)
        {
            Retangulo? lugar = PosicaoNaTela.AoAbrir(agora, primeiraVez: Opcoes.Janela == null, JanelaDoWindows.AreasDosMonitores(),
                JanelaDoWindows.AreaDoMonitorPrincipal());
            if (lugar is Retangulo novo)
                JanelaDoWindows.MoverPara(hwnd, novo);
            else if (PuxarParaDentro(hwnd, agora) is Retangulo dentro)
            {
                // Pelo próprio WPF: ao aparecer, ele põe a janela no Left/Top dele (o gravado), por cima de um SetWindowPos
                double escala = JanelaDoWindows.EscalaDe(hwnd);
                Left = dentro.Esquerda / escala;
                Top = dentro.Topo / escala;
            }
        }
        AplicarEstado(); // o clique atravessa precisa da janela do Windows
    }

    // Gravada menor que o mínimo de agora (de uma versão antiga), a janela cresce para a direita e para baixo: o que
    // cresce para fora do monitor volta para dentro. Aqui ela ainda pode estar com o tamanho gravado (o WPF aplica o
    // mínimo depois), então a conta é feita com o tamanho já crescido
    private Retangulo? PuxarParaDentro(IntPtr hwnd, Retangulo agora)
    {
        if (Opcoes.Janela is not PosicaoDaJanela gravada || JanelaDoWindows.AreaDoMonitorDe(hwnd) is not Retangulo area)
            return null;
        double escala = JanelaDoWindows.EscalaDe(hwnd);
        int minimaX = (int)Math.Ceiling(MinWidth * escala);
        int minimaY = (int)Math.Ceiling(MinHeight * escala);
        int cresceuX = Math.Max(0, minimaX - (int)Math.Round(gravada.Largura * escala));
        int cresceuY = Math.Max(0, minimaY - (int)Math.Round(gravada.Altura * escala));
        if (cresceuX == 0 && cresceuY == 0)
            return null;
        Retangulo crescida = Retangulo.De(agora.Esquerda, agora.Topo, Math.Max(agora.Largura, minimaX), Math.Max(agora.Altura, minimaY));
        return PosicaoNaTela.PuxarParaDentro(crescida, area, cresceuX, cresceuY);
    }

    private async Task PrepararNavegadorAsync()
    {
        if (!NavegadorDoChat.EstaInstalado())
        {
            _temWebView2 = false;
            cartaoFaltaWebView2.Visibility = Visibility.Visible;
            FicarPronta();
            return;
        }

        _temWebView2 = true;
        cartaoFaltaWebView2.Visibility = Visibility.Collapsed;
        await _navegador.CriarAsync();

        if (Opcoes.EsconderBordasAoAbrir)
            OcultarBordas();
        else
            MostrarBordas();
        AtualizarSom();
        _navegador.AbrirDasOpcoes();
        AtualizarYouTube();
        AtualizarKick();
        AtualizarTikTok();
        AtualizarViewers();
        FicarPronta();
    }

    // Os comandos que chegaram antes (argumentos da abertura, outra cópia) são feitos agora (decisão 9)
    private void FicarPronta()
    {
        foreach (ComandoDoApp comando in _logica.FicouPronta())
            Fazer(comando);
    }

    /// <summary>Argumentos desta abertura, ou pedidos de uma cópia aberta depois (ver <see cref="InstanciaUnica"/>).</summary>
    public void Executar(IReadOnlyList<ComandoDoApp> comandos)
    {
        foreach (ComandoDoApp comando in _logica.Receber(comandos))
            Fazer(comando);
    }

    private void Fazer(ComandoDoApp comando)
    {
        switch (comando)
        {
            case ComandoDoApp.MostrarJanela:
                // Minimizada, volta; com "Sempre no topo" só vem para a frente, sem tirar o foco do jogo
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                if (Opcoes.SempreNoTopo)
                    _sempreNaFrente.Trazer();
                else
                    _sempreNaFrente.Ativar();
                break;
            case ComandoDoApp.AlternarBordas:
                AlternarBordas();
                break;
            case ComandoDoApp.AbrirConfiguracoes:
                AbrirConfiguracoes();
                break;
            case ComandoDoApp.RestaurarPosicao:
                RestaurarPosicao();
                break;
        }
    }

    // --- Estado da janela ------------------------------------------------------------------------------------

    /// <summary>Põe na tela o estado das lógicas: bordas, clique atravessa, fundo, moldura, faixa, caixa, canto.</summary>
    private void AplicarEstado()
    {
        bool bordas = _logica.BordasVisiveis;
        _logica.Escrevendo = _caixa.PeloAtalho;
        _logica.EscrevendoNaCaixaDaTwitch = _caixa.NaCaixaDaTwitch;
        _logica.CaixaDoAppNaTela = _caixa.CaixaDoAppNaTela(bordas);

        barraDeCima.Visibility = bordas ? Visibility.Visible : Visibility.Collapsed;
        ResizeMode = bordas ? ResizeMode.CanResize : ResizeMode.CanMinimize;
        ShowInTaskbar = _logica.NaBarraDeTarefas(Opcoes.EsconderIconeDaBarraDeTarefas);

        moldura.BorderThickness = new Thickness(_logica.MolduraLaranja ? 2 : 0);
        moldura.BorderBrush = _logica.MolduraLaranja ? (Brush)FindResource("LaranjaClaro") : Brushes.Transparent;

        IntPtr hwnd = Hwnd;
        if (hwnd != IntPtr.Zero)
        {
            if (_logica.Clicavel)
                JanelaDoWindows.TornarClicavel(hwnd);
            else
                JanelaDoWindows.DeixarCliqueAtravessar(hwnd);
        }

        double margem = _logica.MargemDoChat;
        if (_navegador.Controle is { } pagina)
        {
            pagina.Focusable = _logica.PaginaRecebeFoco;
            pagina.Margin = new Thickness(margem, 0, margem, margem);
            Grid.SetRowSpan(pagina, _logica.LinhasDoChat);
        }
        Grid.SetRowSpan(fundoEscuro, _logica.LinhasDoChat);
        fundoEscuro.Opacity = _logica.OpacidadeDoFundo(Opcoes.Fundo);

        // Sem o WebView2 o cartão "Falta um componente" cobre tudo embaixo da barra laranja
        bool faixa = _temWebView2 && _logica.FaixaDoCanalNaTela(TiposDeChat.Ler(Opcoes.TipoDeChat));
        faixaDoCanal.Visibility = faixa ? Visibility.Visible : Visibility.Collapsed;
        _faixa.Atualizar(faixa);

        caixaDeEscrever.Visibility = _logica.CaixaDoAppNaTela ? Visibility.Visible : Visibility.Collapsed;
        if (!_logica.CaixaDoAppNaTela && caixaDeEscrever.EmotesAbertos)
            caixaDeEscrever.FecharEmotes();
        barraDeCima.MostrarEscrever(_caixa.BotaoEscreverNaTela,_caixa.Aberta(bordas), _caixa.DicaDoBotaoEscrever(bordas));
        MostrarPausa();

        desenhoDoCanto.Visibility = _logica.CantoLivre ? Visibility.Visible : Visibility.Collapsed;
        _canto.Atualizar(_navegador.Controle, _logica.CantoLivre, margem);
        RecortarCantos();
    }

    // O conteúdo recortado no formato arredondado da moldura
    private void RecortarCantos()
    {
        double raio = Math.Max(0, moldura.CornerRadius.TopLeft - moldura.BorderThickness.Left);
        var recorte = new RectangleGeometry(new Rect(0, 0, grade.ActualWidth, grade.ActualHeight), raio, raio);
        recorte.Freeze();
        grade.Clip = recorte;
    }

    // As barras de rolagem (qualquer página), e a rolagem e os links do Padrão, acompanham as bordas e o modo rolagem
    private void AtualizarPagina()
    {
        _navegador.Executar(ContratoComAPagina.BarrasDeRolagem(_logica.BordasVisiveis));
        if (_navegador.Pagina is not ChatPadrao)
            return;

        MostrarViewersNaPagina();
        (bool ligado, bool aviso) = _logica.RolagemDaPagina;
        Atalho? atalho = Opcoes.AtalhoModoRolagem;
        _navegador.Executar(ContratoComAPagina.ModoRolagem(ligado, aviso, Atalho.Existe(atalho) ? atalho.ToString() : string.Empty));
        _navegador.Executar(ContratoComAPagina.LinksClicaveis(_logica.LinksClicaveis));
    }

    private void MostrarBordas()
    {
        _logica.MostrarBordas();
        AplicarEstado();
        AtualizarPagina();
        _avisos.EsconderBordasOcultas();
        _sempreNaFrente.Ativar();
        _sempreNaFrente.Rajada();
        MostrarNovidadeSeFaltar();
    }

    private void OcultarBordas()
    {
        // A caixa da Twitch aberta pelo botão fecha; a do app aberta pelo botão também (e não volta com as bordas)
        if (_caixa.DaTwitchPeloBotao)
            TerminarEscrita(devolverFoco: false);
        _caixa.BordasOcultas();
        _logica.OcultarBordas();
        barraDeCima.FecharPaineis();
        EsconderNovidade(); // volta com as bordas
        AplicarEstado();
        AtualizarPagina();
        _avisos.PedirBordasOcultas();
        MostrarAvisosQueEsperam();
        MostrarAvisoDoOverlayNaPagina(); // o balão não chegou a aparecer
        _sempreNaFrente.Ativar();
        _sempreNaFrente.Rajada();
    }

    private void AlternarBordas()
    {
        if (!_temWebView2)
            return;
        if (_logica.BordasVisiveis)
            OcultarBordas();
        else
            MostrarBordas();
    }

    // Atalho e menu do ícone. Com as bordas visíveis não faz nada (decisão 5)
    private void AlternarModoRolagem()
    {
        if (!_temWebView2 || !_logica.AlternarModoRolagem())
            return;

        AplicarEstado();
        // Não por cima das Configurações abertas
        if (_logica.ModoRolagem && _janelaDeConfiguracoes == null)
            _sempreNaFrente.Ativar();
        AtualizarPagina();
        _sempreNaFrente.Rajada();
    }

    private void SairDoModoRolagem()
    {
        if (!_logica.SairDoModoRolagem())
            return;
        AplicarEstado();
        AtualizarPagina();
        _sempreNaFrente.Rajada();
    }

    private void MostrarAvisosQueEsperam() =>
        _avisos.MostrarOsQueEsperam(!_logica.BordasVisiveis, _janelaDeConfiguracoes != null, Opcoes.AtalhoBordas,
            () => _caixa.TextoQueDa(_logica.BordasVisiveis));

    // --- Sempre no topo, tamanho do texto, fundo -------------------------------------------------------------

    private void AlternarSempreNoTopo()
    {
        Opcoes.SempreNoTopo = !Opcoes.SempreNoTopo;
        _arquivo.Gravar(); // na hora: não se perde se o app não fechar direito
        AplicarSempreNoTopo();
    }

    private void AplicarSempreNoTopo()
    {
        bool ligado = Opcoes.SempreNoTopo;
        _sempreNaFrente.Ligado = ligado;
        barraDeCima.MostrarSempreNoTopo(ligado, Opcoes.AtalhoSempreNoTopo);
        _bandeja.MostrarSempreNoTopo(ligado);
    }

    // O mesmo valor dá letras do mesmo tamanho no Padrão e no chat oficial (que usa a fonte do Padrão)
    private void EscolherTamanhoDoTexto(double valor)
    {
        if (_temWebView2)
        {
            Opcoes.TamanhoDoTexto = _navegador.AplicarZoom(valor);
            _arquivo.GravarDaquiAPouco(); // o deslizante manda muitas mudanças: só a última é gravada
        }
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
    }

    private void EscolherFundo(double porcentagem) => MudarFundo((byte)Math.Round(Math.Clamp(porcentagem, 0, 100) * 2.55));

    // O fundo vai de 0 a 255 no arquivo (165 aparece como 65%)
    private void MudarFundo(byte fundo)
    {
        if (_temWebView2)
        {
            Opcoes.Fundo = fundo;
            fundoEscuro.Opacity = _logica.OpacidadeDoFundo(Opcoes.Fundo);
            _arquivo.GravarDaquiAPouco();
        }
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
    }

    // --- Barra, ícone perto do relógio, atalhos --------------------------------------------------------------

    private void LigarBarra()
    {
        barraDeCima.OcultarBordasPedido += () =>
        {
            if (_temWebView2)
                OcultarBordas();
        };
        barraDeCima.ConfiguracoesPedido += AbrirConfiguracoes;
        barraDeCima.FecharPedido += Close;
        barraDeCima.SempreNoTopoPedido += ligado =>
        {
            if (ligado != Opcoes.SempreNoTopo)
                AlternarSempreNoTopo();
            else
                AplicarSempreNoTopo();
        };
        barraDeCima.EscreverPedido += ClicarEmEscrever;
        barraDeCima.PausarPedido += AlternarPausa;
        barraDeCima.NovidadeEntendida += BalaoEntendido;
        barraDeCima.NovidadeRecusada += BalaoRecusado;
        barraDeCima.MultiplataformaPedido += ligado => _ = AlternarMultiplataformaAsync(ligado);
        barraDeCima.TamanhoDoTextoEscolhido += EscolherTamanhoDoTexto;
        barraDeCima.FundoEscolhido += EscolherFundo;
        barraDeCima.FundoPadraoPedido += () => MudarFundo(Opcoes.FundoPadrao);
    }

    private void LigarBandeja()
    {
        _bandeja.BordasPedido += AlternarBordas;
        _bandeja.ModoRolagemPedido += AlternarModoRolagem;
        _bandeja.SempreNoTopoPedido += AlternarSempreNoTopo;
        _bandeja.ConfiguracoesPedido += AbrirConfiguracoes;
        _bandeja.RestaurarPosicaoPedido += RestaurarPosicao;
        _bandeja.ProcurarAtualizacoesPedido += () => _ = _atualizacoes.ProcurarAsync(manual: true, dono: null);
        _bandeja.SairPedido += Sair;
    }

    // Registrados de novo a cada salvamento: um atalho trocado deixa de valer na hora
    private void DefinirAtalhos()
    {
        _atalhos.Definir("MostrarEsconderBordas", Opcoes.AtalhoBordas, AlternarBordas);
        _atalhos.Definir("ModoRolagem", Opcoes.AtalhoModoRolagem, AlternarModoRolagem);
        _atalhos.Definir("SempreNoTopo", Opcoes.AtalhoSempreNoTopo, AlternarSempreNoTopo);
        _atalhos.Definir("EscreverNoChat", Opcoes.AtalhoEscrever, AtalhoDeEscrever);
        _atalhos.Definir("PausarChat", Opcoes.AtalhoPausar, AlternarPausa);
        // Só com o overlay ligado: sem ele, a combinação fica livre para outros programas
        _atalhos.Definir("LimparOverlay", Opcoes.OverlayLigado ? Opcoes.AtalhoLimparOverlay : null, LimparOverlayPeloAtalho);
        DefinirAtalhosDoAutoMod();
        barraDeCima.MostrarAtalhoDasBordas(Opcoes.AtalhoBordas);
        MostrarPausa();
    }

    // Uma mensagem para a janela antes do WPF
    private IntPtr Mensagens(IntPtr hwnd, int mensagem, IntPtr wParam, IntPtr lParam, ref bool tratada)
    {
        // Pode minimizar pela barra de tarefas, mas nunca maximizar (decisão 18)
        if (JanelaDoWindows.SemMaximizar(mensagem, wParam, lParam))
            return IntPtr.Zero;

        // Com as Configurações abertas o Windows desativa o chat: um clique nele traz a janela aberta para a frente e pisca
        // a moldura dela (sem o som de erro do Windows)
        if (mensagem == WM_SETCURSOR)
        {
            long valor = lParam.ToInt64();
            int ondeClicou = (short)(valor & 0xFFFF);
            int botao = (int)((valor >> 16) & 0xFFFF);
            if (ondeClicou == HTERROR && botao is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                && ChamarAtencao.ParaAJanelaAberta(hwnd, typeof(JanelaChat)))
            {
                tratada = true;
                return new IntPtr(1);
            }
        }
        return IntPtr.Zero;
    }

    // --- Pausar o chat ---------------------------------------------------------------------------------------

    // O botão da barra e o atalho. Só o Padrão pausa: a página para de rolar, guarda as mensagens que chegam embaixo do
    // aviso da pausa e, ao continuar, rola sozinha até a última (browser/chat.js, "Pause"); ela conta o estado novo
    private void AlternarPausa()
    {
        if (!_temWebView2)
            return;
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta)
            _navegador.Executar(ContratoComAPagina.AlternarPausa);
        else if (_navegador.Pagina is ChatOficialDaTwitch or ChatEnderecoPersonalizado)
            _avisos.Mostrar("Pausar o chat só funciona com o tipo de chat Padrão.");
        MostrarPausa(); // o botão volta ao estado de verdade até a página responder
    }

    private void MostrarPausa() =>
        barraDeCima.MostrarPausa(_temWebView2 && _navegador.Pagina is ChatPadrao, _pausa == MensagemDaPagina.ChatPausado,
            _pausa == MensagemDaPagina.ChatContinuando, Opcoes.AtalhoPausar);

    // --- Novidades (o balão que aparece uma vez só) -------------------------------------------------------------

    // Quando pode ter passado a dar para mostrar um balão (a página carregou, as bordas voltaram, as Configurações ou a
    // faixa fecharam, a conta foi conferida, a anterior foi vista): espera um pouco, porque a página acabou de aparecer,
    // e confere de novo
    private void MostrarNovidadeSeFaltar() => MostrarNovidadeSeFaltar(Novidades.Espera);

    private void MostrarNovidadeSeFaltar(TimeSpan espera)
    {
        if (ProximoBalao() == null || barraDeCima.NovidadeAberta)
            return;
        _esperaDaNovidade?.Stop();
        _esperaDaNovidade = new DispatcherTimer { Interval = espera };
        _esperaDaNovidade.Tick += (_, _) =>
        {
            _esperaDaNovidade?.Stop();
            if (barraDeCima.NovidadeAberta)
                return;
            _novidadeNaTela = ProximoBalao();
            if (_novidadeNaTela == AvisoDoOverlay)
            {
                _avisoDoOverlayNaPaginaPendente = false; // o balão já disse
                barraDeCima.MostrarAvisoDoOverlay(_overlay.Estado == EstadoDoOverlay.OutroOlhoNoChat);
            }
            else if (_novidadeNaTela == Novidades.Pausa)
                barraDeCima.MostrarNovidadeDaPausa(Opcoes.AtalhoPausar);
            else if (_novidadeNaTela == Novidades.AutoMod)
                barraDeCima.MostrarNovidadeDoAutoMod(!_conta.EstaConectada ? BarraDeCima.FaltaParaOAutoMod.Conectar
                    : !_conta.PodeModerarAutoMod ? BarraDeCima.FaltaParaOAutoMod.ConectarDeNovo
                    : BarraDeCima.FaltaParaOAutoMod.Nada);
            else if (_novidadeNaTela == Novidades.Overlay)
                barraDeCima.MostrarNovidadeDoOverlay();
        };
        _esperaDaNovidade.Start();
    }

    // O aviso do overlay vem antes das novidades: é um problema agora. Precisa das bordas (o botão "Resolver")
    private string? ProximoBalao() =>
        _balaoDoOverlayPendente && _temWebView2 && !_fechando && WindowState != WindowState.Minimized && _logica.BordasVisiveis
        && !_faixa.EditorAberto && _janelaDeConfiguracoes == null
            ? AvisoDoOverlay
            : ProximaNovidade();

    private string? ProximaNovidade() =>
        !_temWebView2 || _fechando || WindowState == WindowState.Minimized
            ? null
            : Novidades.Proxima(Opcoes.NovidadesVistas,
                // Conectada, espera a conferência das permissões (o balão diz se falta conectar de novo)
                autoModPronto: Opcoes.AprovarAutoMod && (!_conta.EstaConectada || _conta.PermissoesConferidas),
                overlayLigado: Opcoes.OverlayLigado,
                _navegador.Pagina is ChatPadrao && _navegador.PaginaPronta, _logica.BordasVisiveis, _faixa.EditorAberto,
                _janelaDeConfiguracoes != null);

    // O app tira o balão da tela (bordas ocultas, Configurações, outra página, faixa aberta): ele volta quando der
    private void EsconderNovidade()
    {
        _esperaDaNovidade?.Stop();
        barraDeCima.FecharNovidade();
    }

    // O botão principal do balão: "Entendi" numa novidade, "Resolver" no aviso do overlay (abre a aba Overlay)
    private void BalaoEntendido()
    {
        if (_novidadeNaTela != AvisoDoOverlay)
        {
            NovidadeVista();
            return;
        }
        _balaoDoOverlayPendente = false;
        EsconderNovidade();
        _novidadeNaTela = null;
        AbrirConfiguracoes(naAbaOverlay: true);
    }

    // "Agora não" no aviso do overlay: não volta até o overlay abrir e der problema de novo
    private void BalaoRecusado()
    {
        _balaoDoOverlayPendente = false;
        EsconderNovidade();
        _novidadeNaTela = null;
        MostrarNovidadeSeFaltar(Novidades.EsperaDaSeguinte);
    }

    // A pessoa viu: "Entendi", ou pausou com o balão da pausa aberto. Não aparece mais, e a próxima que falta vem em seguida
    private void NovidadeVista()
    {
        string? vista = _novidadeNaTela;
        EsconderNovidade();
        _novidadeNaTela = null;
        if (vista != null && !Opcoes.NovidadesVistas.Contains(vista))
        {
            Opcoes.NovidadesVistas.Add(vista);
            _arquivo.Gravar();
        }
        MostrarNovidadeSeFaltar(Novidades.EsperaDaSeguinte);
    }

    // --- Caixa de escrever -----------------------------------------------------------------------------------

    private bool PaginaDoChatOficialAberta => _navegador.Pagina is ChatOficialDaTwitch && _navegador.Controle != null;

    private void ClicarEmEscrever()
    {
        switch (_caixa.AoClicarNoBotao(PaginaDoChatOficialAberta))
        {
            case LogicaCaixaDeEscrever.AcaoDoBotao.Fechar:
                FecharCaixa(devolverFoco: false);
                break;
            case LogicaCaixaDeEscrever.AcaoDoBotao.AbrirCaixaDaTwitch:
                ComecarEscrita(daTwitchPeloBotao: true); // fica aberta como a do app
                break;
            case LogicaCaixaDeEscrever.AcaoDoBotao.AvisarQueNaoDa:
                AplicarEstado(); // o botão não acende
                _avisos.Mostrar(_caixa.TextoQueNaoDa);
                break;
            case LogicaCaixaDeEscrever.AcaoDoBotao.AbrirCaixaDoApp:
                _caixa.AbrirPeloBotao();
                AplicarEstado();
                caixaDeEscrever.Focar();
                break;
        }
    }

    private void AtalhoDeEscrever()
    {
        if (!_temWebView2)
            return;

        switch (_caixa.AoApertarOAtalho(_logica.BordasVisiveis, IsActive, PaginaDoChatOficialAberta))
        {
            case LogicaCaixaDeEscrever.AcaoDoAtalho.Fechar:
                FecharCaixa(devolverFoco: true);
                break;
            case LogicaCaixaDeEscrever.AcaoDoAtalho.AvisarQueNaoDa:
                _avisos.Mostrar(_caixa.TextoQueNaoDa);
                break;
            case LogicaCaixaDeEscrever.AcaoDoAtalho.Abrir:
                ComecarEscrita(daTwitchPeloBotao: false);
                break;
        }
    }

    // Lembra a janela que estava em foco (o jogo), deixa o chat clicável, ativa o chat e põe o cursor na caixa
    private void ComecarEscrita(bool daTwitchPeloBotao)
    {
        IntPtr eu = Hwnd;
        IntPtr emFoco = JanelaDoWindows.JanelaEmFoco();
        bool naTwitch = _caixa.UsaCaixaDaTwitch;
        _caixa.Comecar(naTwitch, daTwitchPeloBotao, emFoco != eu ? emFoco : IntPtr.Zero);
        AplicarEstado();
        _sempreNaFrente.Ativar();

        if (naTwitch)
        {
            _navegador.Controle?.Focus();
            _navegador.Executar(ContratoComAPagina.AbrirCaixaDaTwitch);
        }
        else
        {
            caixaDeEscrever.Focar();
        }
    }

    // "×", Esc, o botão Escrever ou o atalho
    private void FecharCaixa(bool devolverFoco)
    {
        _caixa.FecharOBotao();
        if (_caixa.PeloAtalho)
            TerminarEscrita(devolverFoco);
        else
            AplicarEstado();
    }

    private void TerminarEscrita(bool devolverFoco)
    {
        if (!_caixa.PeloAtalho)
            return;

        bool naTwitch = _caixa.NaCaixaDaTwitch;
        IntPtr devolver = _caixa.Terminar();
        if (naTwitch && _navegador.Controle != null)
            _navegador.Executar(ContratoComAPagina.FecharCaixaDaTwitch);
        AplicarEstado();

        if (devolverFoco && devolver != IntPtr.Zero)
            JanelaDoWindows.DarFoco(devolver);
    }

    // A conta foi conectada ou desconectada (aqui ou nas Configurações), ou a verificação a invalidou
    private void ContaMudou()
    {
        if (!_conta.EstaConectada)
        {
            TerminarEscrita(devolverFoco: false);
            caixaDeEscrever.DescartarEmotes();
        }
        _ = AtualizarEventSubAsync(); // sem conta desliga; com um acesso novo, assina de novo
        MostrarNovidadeSeFaltar(); // conferida, a conta pode ter a novidade do AutoMod

        _caixa.Atualizar();
        AplicarEstado();
    }

    // --- Canal e página do chat ------------------------------------------------------------------------------

    // Grava os canais na hora e abre o chat deles (ou as boas-vindas, sem canal). Só o do YouTube, o da Kick ou o do TikTok
    // trocado não recarrega o chat (as mensagens da tela ficam): o app lê esses três
    private void TrocarCanal(string canal, string youTube, string kick, string tikTok)
    {
        _log.LogInformation("Canal trocado na faixa do canal.");
        // Outro canal da Twitch: a página começa vazia, e o overlay também
        if (!string.Equals(Opcoes.Canal, canal, StringComparison.OrdinalIgnoreCase))
            _overlay.Limpar();
        Opcoes.Canal = canal;
        Opcoes.CanalDoYouTube = youTube;
        Opcoes.CanalDaKick = kick;
        Opcoes.CanalDoTikTok = tikTok;
        _arquivo.Gravar();
        _ = _navegador.AplicarOpcoesSalvasAsync(); // recarrega só se a página for outra (outra Twitch, sem canal...)
        AtualizarYouTube();
        AtualizarKick();
        AtualizarTikTok();
        AtualizarViewers();
        _caixa.Atualizar();
        AplicarEstado();
    }

    // --- Overlay da live --------------------------------------------------------------------------------------

    // O overlay é uma Fonte de navegador do OBS (Overlay/ServidorDoOverlay.cs) com as linhas que o Padrão mostrou: a
    // página manda cada uma para cá, e o servidor passa para o OBS
    private void LigarOverlay()
    {
        _navegador.ParaOOverlay += pedido =>
        {
            if (!Opcoes.OverlayLigado)
                return;
            switch (pedido.Tipo)
            {
                case ContratoDoOverlay.TipoDoPedido.Linha:
                    _overlay.EnviarLinha(pedido.Valor);
                    break;
                case ContratoDoOverlay.TipoDoPedido.Remover:
                    _overlay.EnviarRemocao(pedido.Valor);
                    break;
                case ContratoDoOverlay.TipoDoPedido.Teste:
                    _overlay.EnviarTeste(pedido.Valor);
                    break;
                case ContratoDoOverlay.TipoDoPedido.Substituir:
                    _overlay.EnviarSubstituicao(pedido.Valor, pedido.Html);
                    break;
                case ContratoDoOverlay.TipoDoPedido.Limpar:
                    _overlay.Limpar();
                    break;
            }
        };
        _overlay.EstadoMudou += _ => Dispatcher.BeginInvoke(OverlayMudou);
        _overlay.ConectadosMudaram += () => Dispatcher.BeginInvoke(() => _janelaDeConfiguracoes?.Logica.Overlay.AtualizarEstado());
    }

    // Abre (ou continua aberto, com as opções novas) só com a opção ligada; a porta é a gravada
    private void AtualizarOverlay()
    {
        if (Opcoes.OverlayLigado)
            _overlay.Ligar(Opcoes.PortaDoOverlay, ContratoDoOverlay.JsonDasOpcoes(Opcoes));
        else
            _overlay.Desligar();
    }

    private void OverlayMudou()
    {
        _janelaDeConfiguracoes?.Logica.Overlay.AtualizarEstado();
        if (_overlay.Estado is EstadoDoOverlay.PortaOcupada or EstadoDoOverlay.OutroOlhoNoChat)
        {
            if (_problemaDoOverlayAvisado)
                return;
            _problemaDoOverlayAvisado = true;
            _balaoDoOverlayPendente = true;
            _avisoDoOverlayNaPaginaPendente = true;
            MostrarAvisoDoOverlayNaPagina();
            MostrarNovidadeSeFaltar();
            return;
        }
        // Abriu (o outro programa soltou a porta, outro endereço) ou foi desligado: o balão sai sozinho
        _problemaDoOverlayAvisado = false;
        _balaoDoOverlayPendente = false;
        _avisoDoOverlayNaPaginaPendente = false;
        if (_novidadeNaTela == AvisoDoOverlay)
        {
            EsconderNovidade();
            _novidadeNaTela = null;
            MostrarNovidadeSeFaltar();
        }
    }

    // Sem as bordas o balão não aparece (o botão dele não daria para clicar): um aviso na página diz como chegar nele
    private void MostrarAvisoDoOverlayNaPagina()
    {
        // Com as bordas, o balão cuida; ele tira este pedido quando aparecer (as bordas podem sumir antes disso)
        if (!_avisoDoOverlayNaPaginaPendente || _logica.BordasVisiveis || !_navegador.PaginaPronta)
            return;
        _avisoDoOverlayNaPaginaPendente = false;
        Atalho? bordas = Opcoes.AtalhoBordas;
        _avisos.Mostrar("O overlay da live não abriu: o endereço dele está ocupado. Mostre as bordas" +
                        (Atalho.Existe(bordas) ? $" ({bordas})" : string.Empty) + " para ver como resolver.");
    }

    // "Mensagens de teste": quem monta é a página do Padrão, com o mesmo código das mensagens de verdade
    private void MandarTesteDoOverlay()
    {
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta)
            _navegador.Executar(ContratoComAPagina.TesteDoOverlay);
    }

    private void LimparOverlayPeloAtalho()
    {
        _overlay.Limpar();
        _avisos.Mostrar("Overlay da live limpo.");
    }

    // "Encontrar outro endereço" (aba Overlay): grava a porta nova e abre o overlay nela
    private int? EncontrarOutraPortaDoOverlay()
    {
        int? nova = ServidorDoOverlay.EncontrarPortaLivre(Opcoes.PortaDoOverlay);
        if (nova == null)
            return null;
        Opcoes.PortaDoOverlay = nova.Value;
        _arquivo.Gravar();
        AtualizarOverlay();
        return nova;
    }

    // --- YouTube (Chat Multiplataforma) ----------------------------------------------------------------------

    private void LigarYouTube()
    {
        _youTube.EstadoMudou += estado => Dispatcher.BeginInvoke(() =>
        {
            _faixa.ConexaoDoYouTube = estado;
            ContarAPaginaOsEstados();
            _viewers.ContarAgora(); // a live começou ou acabou
        });
        _youTube.MensagemRecebida += item =>
        {
            if (MensagemParaAPagina.De(item) is { } mensagem)
                Dispatcher.BeginInvoke(() => MostrarDoYouTube(mensagem));
        };
    }

    // Lê o YouTube só com o Chat Multiplataforma ligado e um canal do YouTube (as mensagens vão para a página do Padrão)
    private void AtualizarYouTube()
    {
        CanalDoYouTube? canal = ChatMultiplataforma.CanalALer(Opcoes);
        _youTube.MostrarHistorico = Opcoes.MostrarHistoricoDoYouTube; // vale na próxima vez que conectar
        if (canal != null && _temWebView2)
            _youTube.Ligar(canal);
        else
            _youTube.Desligar();
    }

    // O botão da barra: liga o Chat Multiplataforma (só no Padrão: outro tipo de chat vira o Padrão) ou volta a só Twitch,
    // como o interruptor de Configurações > Chat. Sem canal do YouTube, da Kick nem do TikTok, a faixa abre na caixa do YouTube.
    private async Task AlternarMultiplataformaAsync(bool ligado)
    {
        if (!_temWebView2 || ligado == Opcoes.ChatMultiplataforma)
        {
            barraDeCima.MostrarMultiplataforma(Opcoes.ChatMultiplataforma);
            return;
        }

        _log.LogInformation("Chat Multiplataforma {Estado} pela barra.", ligado ? "ligado" : "desligado");
        Opcoes.ChatMultiplataforma = ligado;
        if (ligado)
            Opcoes.TipoDeChat = (int)TipoDeChat.Padrao;
        _arquivo.Gravar();
        await AplicarOpcoesSalvasAsync();

        if (ligado && !ChatMultiplataforma.TemOutroCanal(Opcoes) && PaginaDoChat.CanalSalvo(Opcoes).Length > 0)
            _faixa.AbrirEditorNoYouTube();
    }

    private void MostrarDoYouTube(MensagemParaAPagina mensagem)
    {
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta && Opcoes.ChatMultiplataforma)
            _navegador.Executar(ContratoComAPagina.AdicionarDoYouTube(mensagem));
    }

    // --- Kick (Chat Multiplataforma) -------------------------------------------------------------------------

    private void LigarKick()
    {
        _kick.EstadoMudou += estado => Dispatcher.BeginInvoke(() =>
        {
            _faixa.ConexaoDaKick = estado;
            ContarAPaginaOsEstados();
        });
        // Uma mensagem nova, ou uma apagada, um banimento e o chat limpo (as linhas saem da tela)
        _kick.EventoRecebido += evento => Dispatcher.BeginInvoke(() =>
            NaPaginaDaPlataforma(evento.Mensagem is { } item ? ContratoComAPagina.AdicionarDaKick(item) : ContratoComAPagina.ApagarDaKick(evento)));
    }

    // Lê a Kick só com o Chat Multiplataforma ligado e um canal da Kick (as mensagens vão para a página do Padrão)
    private void AtualizarKick()
    {
        string? canal = ChatMultiplataforma.KickALer(Opcoes);
        if (canal != null && _temWebView2)
            _kick.Ligar(canal);
        else
            _kick.Desligar();
    }

    // --- TikTok (Chat Multiplataforma) -----------------------------------------------------------------------

    private void LigarTikTok()
    {
        _tikTok.EstadoMudou += estado => Dispatcher.BeginInvoke(() =>
        {
            _faixa.ConexaoDoTikTok = estado;
            ContarAPaginaOsEstados();
            if (estado != EstadoDoTikTok.Conectado)
                _viewers.TikTok(null);
        });
        _tikTok.MensagemRecebida += item => Dispatcher.BeginInvoke(() => NaPaginaDaPlataforma(ContratoComAPagina.AdicionarDoTikTok(item)));
        _tikTok.MensagensApagadas += (ids, logins) =>
            Dispatcher.BeginInvoke(() => NaPaginaDaPlataforma(ContratoComAPagina.ApagarDoTikTok(ids, logins)));
        // Presentes, seguidores, compartilhamentos e Superfãs: a página esconde os desligados (cada um tem o seu interruptor)
        _tikTok.AvisoRecebido += aviso => Dispatcher.BeginInvoke(() => NaPaginaDaPlataforma(ContratoComAPagina.AdicionarAvisoDoTikTok(aviso)));
        // A conta do TikTok (aba TikTok) conectou ou desconectou: conecta de novo, com ou sem ela. Recusada pelo TikTok, a
        // leitura já seguiu sem ela: só avisa, uma vez
        _contaDoTikTok.Mudou += () => Dispatcher.BeginInvoke(() =>
        {
            if (_contaDoTikTok.Conta == null && _contaDoTikTok.FoiRecusada)
                NaPaginaDaPlataforma(ContratoComAPagina.AvisoDoApp(
                    "TikTok: a conta desconectou (o TikTok não aceita mais a sessão). Conecte de novo em Configurações › TikTok."));
            else
                _tikTok.Recomecar();
        });
    }

    // Lê o TikTok só com o Chat Multiplataforma ligado e um canal do TikTok (as mensagens vão para a página do Padrão)
    private void AtualizarTikTok()
    {
        string? canal = ChatMultiplataforma.TikTokALer(Opcoes);
        _tikTok.MostrarHistorico = Opcoes.MostrarHistoricoDoYouTube; // a mesma opção do YouTube; vale na próxima vez que conectar
        if (canal != null && _temWebView2)
            _tikTok.Ligar(canal);
        else
            _tikTok.Desligar();
    }

    // --- Viewers ("Mostrar viewers", aba Chat) ----------------------------------------------------------------

    private void LigarViewers()
    {
        _viewers.Mudou += _ => Dispatcher.BeginInvoke(MostrarOsViewers);
        _tikTok.ViewersMudou += viewers => _viewers.TikTok(viewers);
    }

    // Conta os canais da faixa (o YouTube, a Kick e o TikTok só no Chat Multiplataforma), só com a opção ligada
    private void AtualizarViewers()
    {
        bool ligado = Opcoes.MostrarViewers && _temWebView2;
        string twitch = PaginaDoChat.CanalSalvo(Opcoes);
        _viewers.Contar(ligado && twitch.Length > 0 ? twitch : null,
            ligado && ChatMultiplataforma.CanalALer(Opcoes) != null,
            ligado ? ChatMultiplataforma.KickALer(Opcoes) : null,
            ligado && ChatMultiplataforma.TikTokALer(Opcoes) != null);
        MostrarOsViewers();
    }

    // Com as bordas, na faixa do canal; sem elas, no topo do Padrão
    private void MostrarOsViewers()
    {
        _faixa.Viewers = Opcoes.MostrarViewers ? _viewers.Atual : Viewers.Nenhum;
        MostrarViewersNaPagina();
    }

    private void MostrarViewersNaPagina()
    {
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta)
            _navegador.Executar(ContratoComAPagina.MostrarViewers(
                Opcoes.MostrarViewers && !_logica.BordasVisiveis ? _viewers.Atual : null));
    }

    // Chat Multiplataforma: o aviso de conexão da página junta todas as plataformas; a da Twitch ela mesma lê, a do
    // YouTube, a da Kick e a do TikTok o app conta (a cada mudança e quando a página carrega)
    private void ContarAPaginaOsEstados()
    {
        if (_navegador.Pagina is not ChatPadrao || !_navegador.PaginaPronta || !Opcoes.ChatMultiplataforma)
            return;
        _navegador.Executar(ContratoComAPagina.EstadoDaPlataforma("youtube", ContratoComAPagina.EstadoParaAPagina(_youTube.Estado)));
        _navegador.Executar(ContratoComAPagina.EstadoDaPlataforma("kick", ContratoComAPagina.EstadoParaAPagina(_kick.Estado)));
        _navegador.Executar(ContratoComAPagina.EstadoDaPlataforma("tiktok", ContratoComAPagina.EstadoParaAPagina(_tikTok.Estado)));
    }

    // Uma mensagem do YouTube, da Kick ou do TikTok, só na página do Padrão com o Chat Multiplataforma
    private void NaPaginaDaPlataforma(string? script)
    {
        if (script == null)
            return;
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta && Opcoes.ChatMultiplataforma)
            _navegador.Executar(script);
    }

    private void LigarNavegador()
    {
        // O controle novo (também depois de uma queda) fica longe das bordas de redimensionar
        _navegador.ControleCriado += controle =>
        {
            controle.SizeChanged += (_, _) => _canto.Atualizar(_navegador.Controle, _logica.CantoLivre, _logica.MargemDoChat);
            AplicarEstado();
        };

        _navegador.ComecouACarregar += () =>
        {
            // A caixa da Twitch vai embora com a página dela
            if (_caixa.NaCaixaDaTwitch)
                TerminarEscrita(devolverFoco: false);
            _faixa.PaginaComecouACarregar(_navegador.Pagina?.Canal != null);
            _pausa = MensagemDaPagina.ChatAoVivo; // a página nova começa acompanhando as mensagens
            MostrarPausa();
            EsconderNovidade(); // volta quando a página nova carregar, se ela for a do Padrão
        };

        _navegador.Carregou += sucesso =>
        {
            _faixa.PaginaCarregou(sucesso, _navegador.Pagina is ChatOficialDaTwitch);
            if (!sucesso)
                return;

            barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo); // o tamanho salvo pode ter sido ajustado aos limites
            AtualizarPagina();
            ContarAPaginaOsEstados();
            MostrarAvisosQueEsperam();
            MostrarAutoMod(aviso: null); // a página nova mostra a fila (o mesmo canal: as mensagens continuam esperando)
            _ = AtualizarEventSubAsync();
            MostrarAvisoDoOverlayNaPagina();
            MostrarNovidadeSeFaltar();
        };

        _navegador.Mensagem += mensagem =>
        {
            switch (mensagem)
            {
                case MensagemDaPagina.TocarSom:
                    _som.Tocar();
                    break;
                case MensagemDaPagina.Conectando or MensagemDaPagina.Conectado or MensagemDaPagina.Desconectado
                    when _navegador.Pagina is ChatPadrao:
                    _faixa.PaginaAvisou(mensagem switch
                    {
                        MensagemDaPagina.Conectado => EstadoDaConexao.Conectado,
                        MensagemDaPagina.Desconectado => EstadoDaConexao.SemConexao,
                        _ => EstadoDaConexao.Conectando,
                    });
                    break;
                case MensagemDaPagina.SairDoModoRolagem:
                    SairDoModoRolagem();
                    break;
                case MensagemDaPagina.ChatPausado or MensagemDaPagina.ChatContinuando or MensagemDaPagina.ChatAoVivo
                    when _navegador.Pagina is ChatPadrao:
                    _pausa = mensagem;
                    MostrarPausa();
                    // Pausou (o botão ou o atalho) com o balão da novidade da pausa aberto: já aprendeu
                    if (mensagem == MensagemDaPagina.ChatPausado && barraDeCima.NovidadeAberta && _novidadeNaTela == Novidades.Pausa)
                        NovidadeVista();
                    break;
                case MensagemDaPagina.EscritaEnviada or MensagemDaPagina.EscritaCancelada:
                    // A caixa da Twitch: Esc fecha; enviar fecha só se aberta pelo atalho com "Fechar a caixa depois de enviar"
                    if (_caixa.NaCaixaDaTwitch && (mensagem == MensagemDaPagina.EscritaCancelada
                                                   || (Opcoes.FecharCaixaDepoisDeEnviar && !_caixa.DaTwitchPeloBotao)))
                        TerminarEscrita(devolverFoco: true);
                    break;
            }
        };

        _navegador.PedidoDoAutoMod += (id, aprovar) => _ = _autoMod.DecidirAsync(id, aprovar);

        _navegador.FalhouDeVez += texto =>
        {
            _atalhos.Ligados = false; // o chat antigo não pode mais ser usado
            MessageBox.Show(texto, "Falha na recuperação", MessageBoxButton.OK, MessageBoxImage.Stop);
            Sair();
        };
    }

    // Os resgates são do canal da conta: só aparecem quando o chat aberto é o desse canal (e só o Padrão os mostra)
    private void MostrarResgate(EventSubDaTwitch.Resgate resgate)
    {
        if (_navegador.Pagina is not ChatPadrao || !ChatMultiplataforma.ResgatesLigados(Opcoes)
            || !string.Equals(_caixa.Canal, _conta.Login, StringComparison.OrdinalIgnoreCase))
            return;

        const string cor = "#a1b3c4";
        _navegador.Executar(ContratoComAPagina.AdicionarAcao(resgate.Nome ?? string.Empty, cor, resgate.Texto));
        if (!string.IsNullOrEmpty(resgate.TextoDigitado))
            _navegador.Executar(ContratoComAPagina.AdicionarAcao(resgate.Nome ?? string.Empty, cor, resgate.TextoDigitado));
    }

    // O que o EventSub recebe: os resgates (do canal da conta) e o AutoMod do canal aberto no Padrão. Depois de a página
    // carregar, de salvar e de a conta mudar; o acesso salvo é conferido antes (uma vez por execução)
    private async Task AtualizarEventSubAsync()
    {
        int versao = ++_versaoDoEventSub;
        bool querResgates = ChatMultiplataforma.ResgatesLigados(Opcoes);
        string? canalDoAutoMod = Opcoes.AprovarAutoMod && _navegador.Pagina is ChatPadrao ? _navegador.Pagina.Canal : null;
        if (!querResgates && canalDoAutoMod == null)
        {
            _autoMod.TrocarDeCanal(null);
            _eventSub.Desligar();
            return;
        }

        if (!await _conta.VerificarUmaVezAsync() || versao != _versaoDoEventSub)
            return;

        string? idDoAutoMod = null;
        if (canalDoAutoMod != null && _conta.PodeModerarAutoMod)
        {
            try
            {
                idDoAutoMod = string.Equals(canalDoAutoMod, _conta.Login, StringComparison.OrdinalIgnoreCase)
                    ? _conta.Id
                    : await _api.IdDoCanalAsync(_conta.Token, canalDoAutoMod);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AutoMod: não deu para achar o id do canal.");
            }
            if (versao != _versaoDoEventSub)
                return;
        }

        _autoMod.TrocarDeCanal(idDoAutoMod);
        await _eventSub.LigarAsync(new EventSubDaTwitch.Pedido(querResgates, idDoAutoMod));
    }

    // --- AutoMod ---------------------------------------------------------------------------------------------

    // As mensagens seguradas no canal aberto: um cartão embaixo do chat do Padrão mostra a mais antiga (browser/chat.js,
    // "AutoMod"); os botões dele (com as bordas) e os atalhos (sem) aprovam ou recusam
    private void LigarAutoMod()
    {
        _eventSub.MensagemFoiRetida += mensagem => Dispatcher.InvokeAsync(() => _autoMod.Receber(mensagem));
        _eventSub.MensagemDecidida += decisao => Dispatcher.InvokeAsync(() => _autoMod.Decidida(decisao));
        _autoMod.Mudou += MostrarAutoMod;
        _autoMod.TemMensagensMudou += _ => DefinirAtalhosDoAutoMod();
    }

    private void MostrarAutoMod(string? aviso)
    {
        if (_navegador.Pagina is ChatPadrao && _navegador.PaginaPronta)
            _navegador.Executar(ContratoComAPagina.MostrarAutoMod(_autoMod, Opcoes.AtalhoAprovarAutoMod, Opcoes.AtalhoRecusarAutoMod, aviso));
    }

    // Só enquanto há mensagem esperando: sem nada na fila, as combinações ficam livres para os outros programas
    private void DefinirAtalhosDoAutoMod()
    {
        bool esperando = _autoMod.Primeira != null;
        _atalhos.Definir("AprovarAutoMod", esperando ? Opcoes.AtalhoAprovarAutoMod : null, () => _ = _autoMod.DecidirAsync(null, aprovar: true));
        _atalhos.Definir("RecusarAutoMod", esperando ? Opcoes.AtalhoRecusarAutoMod : null, () => _ = _autoMod.DecidirAsync(null, aprovar: false));
    }

    // O som de mensagem nova do Padrão (o único tipo com som). Também a cada salvamento: o som, o volume ou a saída podem
    // ter mudado
    private void AtualizarSom()
    {
        string? arquivo = Opcoes.TipoDeChat == (int)TipoDeChat.Padrao
            ? SonsDisponiveis.Caminho(Opcoes.PastaDosSons, Opcoes.SomDeMensagem)
            : null;

        Exception? erro = _som.Configurar(arquivo, Opcoes.Volume, Opcoes.SaidaDeSom, Opcoes.NomeDaSaidaDeSom ?? string.Empty,
            Opcoes.SegundosEntreSons);
        if (erro != null)
        {
            MessageBox.Show($"Não foi possível carregar o arquivo de som: {arquivo}\n\n{erro.Message}",
                "Erro ao carregar som", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // A saída de som gravada não existe mais (ou o Windows a recusou): o aviso já passou para a padrão do Windows
    private void GravarSaidaDeSomPadrao()
    {
        Opcoes.SaidaDeSom = TocadorDeAviso.Padrao;
        Opcoes.NomeDaSaidaDeSom = TocadorDeAviso.NomeDaPadraoGravado;
        _arquivo.Gravar();
    }

    // --- Configurações ---------------------------------------------------------------------------------------

    private void AbrirConfiguracoes() => AbrirConfiguracoes(naAbaOverlay: false);

    private void AbrirConfiguracoes(bool naAbaOverlay)
    {
        if (!_temWebView2)
        {
            MessageBox.Show("Para usar o Olho no Chat, baixe e instale o WebView2 da Microsoft.\nDepois de instalar, abra o app de novo.",
                "WebView2 necessário", MessageBoxButton.OK, MessageBoxImage.Error);
            AbrirNoWindows.Site(CartaoFaltaWebView2.EnderecoDoInstalador);
            return;
        }

        // Só uma janela de Configurações: pedir de novo traz a aberta para a frente
        if (_janelaDeConfiguracoes != null)
        {
            _janelaDeConfiguracoes.Activate();
            if (naAbaOverlay)
                _janelaDeConfiguracoes.MostrarOverlay();
            return;
        }

        // Os atalhos voltam para o Windows enquanto Configurações está aberta: a mesma combinação pode ser gravada na caixa
        // de atalho sem disparar a ação
        _atalhos.Ligados = false;

        var logica = new LogicaConfiguracoes(_arquivo, new LogicaTwitch(_autorizacao, _conta, _eventSub), new LogicaTikTok(_contaDoTikTok),
            overlay: new AcoesDoOverlay(() => _overlay.Estado, () => _overlay.Conectados, _overlay.Limpar,
                MandarTesteDoOverlay, EncontrarOutraPortaDoOverlay));
        var janela = new JanelaConfiguracoes(logica, this);
        if (naAbaOverlay)
            janela.MostrarOverlay();
        _janelaDeConfiguracoes = janela;
        logica.ProcurarAtualizacoesPedido += () => _ = _atualizacoes.ProcurarAsync(manual: true, dono: janela);
        // Uma procura que já está rodando (a da abertura do app, a do menu perto do relógio) também aparece no botão
        logica.Geral.Procura = _atualizacoes.Estado;

        // Trocar de um tipo sem canal para um com canal mostra, depois, o aviso de que dá para escrever
        int tipoSalvo = Opcoes.TipoDeChat;
        logica.Salvou += () =>
        {
            bool antesSemCanal = !TiposDeChat.UsaCanal(TiposDeChat.Ler(tipoSalvo));
            tipoSalvo = Opcoes.TipoDeChat;
            if (antesSemCanal && TiposDeChat.UsaCanal(TiposDeChat.Ler(tipoSalvo)))
                _avisos.PedirDaParaEscrever();
            _ = AplicarOpcoesSalvasAsync();
        };

        EsconderNovidade(); // ficaria por cima das Configurações
        try
        {
            janela.ShowDialog();
        }
        finally
        {
            _janelaDeConfiguracoes = null;
        }
        MostrarAvisosQueEsperam();
        _atalhos.Ligados = true;
        MostrarNovidadeSeFaltar();
    }

    // "Salvar" nas Configurações (ou nos Filtros do chat): aplica na hora, recarregando o chat só quando precisa
    private async Task AplicarOpcoesSalvasAsync()
    {
        await _navegador.AplicarOpcoesSalvasAsync();
        AtualizarSom();
        AtualizarYouTube();
        AtualizarKick();
        AtualizarTikTok();
        AtualizarViewers();
        AtualizarOverlay();
        ContarAPaginaOsEstados(); // o Chat Multiplataforma pode ter sido ligado agora

        _ = AtualizarEventSubAsync();
        MostrarAutoMod(aviso: null); // os atalhos podem ser outros

        // Tamanho do texto, fundo e "Sempre no topo" mudam aqui só com "Restaurar tudo para o padrão"
        Opcoes.TamanhoDoTexto = _navegador.AplicarZoom(Opcoes.TamanhoDoTexto);
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
        AplicarSempreNoTopo();
        barraDeCima.MostrarMultiplataforma(Opcoes.ChatMultiplataforma);
        _caixa.Atualizar();
        AplicarEstado();
        AtualizarPagina();
        DefinirAtalhos(); // continuam desligados até Configurações fechar
    }

    // --- Posição, fechar -------------------------------------------------------------------------------------

    // Menu do ícone, jump list e /resetwindow
    private void RestaurarPosicao()
    {
        MostrarBordas();
        WindowState = WindowState.Normal;
        Left = 10;
        Top = 10;
        Width = 416; // o mesmo tamanho da primeira abertura (decisão 7; 416 desde o botão de pausar)
        Height = 530;

        if (MessageBox.Show("Abrir a pasta de configurações?", "Pasta de configurações", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes)
        {
            AbrirNoWindows.Pasta(_arquivo.Pasta);
        }
    }

    // Minimizada, a posição é a de antes de minimizar; ela volta normal na próxima abertura
    private PosicaoDaJanela PosicaoAtual()
    {
        Rect lugar = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new PosicaoDaJanela(lugar.Left, lugar.Top, lugar.Width, lugar.Height, WindowState.Normal);
    }

    private void GravarAoFechar()
    {
        _fechando = true;
        Opcoes.Janela = PosicaoAtual();
        _arquivo.Gravar();
    }

    // "Sair" no menu do ícone: fecha o app inteiro, também com as Configurações abertas
    private void Sair()
    {
        GravarAoFechar();
        Application.Current.Shutdown();
    }
}
