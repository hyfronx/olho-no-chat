using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Inicio;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Sistema;
using OlhoNoChat.Som;
using OlhoNoChat.Twitch;
using OlhoNoChat.Atalhos;

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
    private readonly ResgatesDePontos _resgates;
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
    private bool _temWebView2;
    private bool _fechando;

    public JanelaChat(ILogger<JanelaChat> log, ArquivoDeConfiguracoes arquivo, ContaDaTwitch conta, AutorizacaoNoNavegador autorizacao,
        EnvioDeMensagem envio, ListaDeEmotes listaDeEmotes, ImagensDeEmotes imagensDeEmotes, ResgatesDePontos resgates, ProcuraDeAtualizacoes atualizacoes,
        IconeDaBandeja bandeja)
    {
        InitializeComponent();

        _log = log;
        _arquivo = arquivo;
        _conta = conta;
        _autorizacao = autorizacao;
        _resgates = resgates;
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
            arquivo.Aviso);
        _sempreNaFrente = new SempreNaFrente(this, () => !_fechando && _janelaDeConfiguracoes == null);
        _canto = new CantoDeRedimensionar(this, () => _logica.BordasVisiveis && WindowState == WindowState.Normal);
        cartaoFaltaWebView2.Instalado += () => _ = PrepararNavegadorAsync();

        LigarBarra();
        LigarBandeja();
        LigarNavegador();
        _resgates.Resgatado += resgate => Dispatcher.InvokeAsync(() => MostrarResgate(resgate));
        _som.SaidaVoltouParaAPadrao += GravarSaidaDeSomPadrao;
        _conta.Mudou += () => Dispatcher.BeginInvoke(ContaMudou);
        // "Atualizar agora": o Velopack fecha o app sem passar pelo Closing, então a posição vai para as opções antes
        _atualizacoes.AntesDeReiniciar = () => Opcoes.Janela = PosicaoAtual();
        _atualizacoes.ProcuraAutomaticaDesligada += () => _janelaDeConfiguracoes?.Logica.ProcuraAutomaticaDesligada();

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
            _resgates.Desligar();
            _som.Dispose();
            _sempreNaFrente.Dispose();
        };
        grade.SizeChanged += (_, _) => RecortarCantos();

        DefinirAtalhos();
        AplicarSempreNoTopo();
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
        AplicarEstado();
        _ = PrepararNavegadorAsync();
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
        }
        AplicarEstado(); // o clique atravessa precisa da janela do Windows
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
        barraDeCima.MostrarEscrever(_caixa.TipoTemCanal, _caixa.Aberta(bordas), _caixa.DicaDoBotaoEscrever(bordas));

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
    }

    private void OcultarBordas()
    {
        // A caixa da Twitch aberta pelo botão fecha; a do app aberta pelo botão também (e não volta com as bordas)
        if (_caixa.DaTwitchPeloBotao)
            TerminarEscrita(devolverFoco: false);
        _caixa.BordasOcultas();
        _logica.OcultarBordas();
        barraDeCima.FecharPaineis();
        AplicarEstado();
        AtualizarPagina();
        _avisos.PedirBordasOcultas();
        MostrarAvisosQueEsperam();
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
        barraDeCima.MostrarAtalhoDasBordas(Opcoes.AtalhoBordas);
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
            _resgates.Desligar();
            TerminarEscrita(devolverFoco: false);
            caixaDeEscrever.DescartarEmotes();
        }
        else if (Opcoes.MostrarResgates)
        {
            _ = _resgates.LigarAsync(); // com um acesso novo, assina de novo
        }

        _caixa.Atualizar();
        AplicarEstado();
    }

    // --- Canal e página do chat ------------------------------------------------------------------------------

    // Grava o canal na hora e abre o chat dele (ou as boas-vindas, sem canal)
    private void TrocarCanal(string canal)
    {
        _log.LogInformation("Canal trocado na faixa do canal.");
        Opcoes.Canal = canal;
        _arquivo.Gravar();
        _navegador.AbrirDasOpcoes();
        _caixa.Atualizar();
        AplicarEstado();
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
        };

        _navegador.Carregou += sucesso =>
        {
            _faixa.PaginaCarregou(sucesso, _navegador.Pagina is ChatOficialDaTwitch);
            if (!sucesso)
                return;

            barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo); // o tamanho salvo pode ter sido ajustado aos limites
            AtualizarPagina();
            MostrarAvisosQueEsperam();
            _ = LigarResgatesAsync();
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
                case MensagemDaPagina.EscritaEnviada or MensagemDaPagina.EscritaCancelada:
                    // A caixa da Twitch: Esc fecha; enviar fecha só se aberta pelo atalho com "Fechar a caixa depois de enviar"
                    if (_caixa.NaCaixaDaTwitch && (mensagem == MensagemDaPagina.EscritaCancelada
                                                   || (Opcoes.FecharCaixaDepoisDeEnviar && !_caixa.DaTwitchPeloBotao)))
                        TerminarEscrita(devolverFoco: true);
                    break;
            }
        };

        _navegador.FalhouDeVez += texto =>
        {
            _atalhos.Ligados = false; // o chat antigo não pode mais ser usado
            MessageBox.Show(texto, "Falha na recuperação", MessageBoxButton.OK, MessageBoxImage.Stop);
            Sair();
        };
    }

    // Os resgates são do canal da conta: só aparecem quando o chat aberto é o desse canal (e só o Padrão os mostra)
    private void MostrarResgate(ResgatesDePontos.Resgate resgate)
    {
        if (_navegador.Pagina is not ChatPadrao || !string.Equals(_caixa.Canal, _conta.Login, StringComparison.OrdinalIgnoreCase))
            return;

        const string cor = "#a1b3c4";
        _navegador.Executar(ContratoComAPagina.AdicionarAcao(resgate.Nome ?? string.Empty, cor, resgate.Texto));
        if (!string.IsNullOrEmpty(resgate.TextoDigitado))
            _navegador.Executar(ContratoComAPagina.AdicionarAcao(resgate.Nome ?? string.Empty, cor, resgate.TextoDigitado));
    }

    // Depois de a página carregar e de verificar o acesso salvo à Twitch
    private async Task LigarResgatesAsync()
    {
        if (Opcoes.MostrarResgates && await _conta.VerificarUmaVezAsync() && Opcoes.MostrarResgates)
            await _resgates.LigarAsync();
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

    private void AbrirConfiguracoes()
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
            return;
        }

        // Os atalhos voltam para o Windows enquanto Configurações está aberta: a mesma combinação pode ser gravada na caixa
        // de atalho sem disparar a ação
        _atalhos.Ligados = false;

        var logica = new LogicaConfiguracoes(_arquivo, new LogicaTwitch(_autorizacao, _conta, _resgates));
        var janela = new JanelaConfiguracoes(logica, this);
        _janelaDeConfiguracoes = janela;
        logica.ProcurarAtualizacoesPedido += () => _ = _atualizacoes.ProcurarAsync(manual: true, dono: janela);

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
    }

    // "Salvar" nas Configurações (ou nos Filtros do chat): aplica na hora, recarregando o chat só quando precisa
    private async Task AplicarOpcoesSalvasAsync()
    {
        await _navegador.AplicarOpcoesSalvasAsync();
        AtualizarSom();

        if (Opcoes.MostrarResgates)
            _ = LigarResgatesAsync();
        else
            _resgates.Desligar();

        // Tamanho do texto, fundo e "Sempre no topo" mudam aqui só com "Restaurar tudo para o padrão"
        Opcoes.TamanhoDoTexto = _navegador.AplicarZoom(Opcoes.TamanhoDoTexto);
        barraDeCima.MostrarValores(Opcoes.TamanhoDoTexto, Opcoes.Fundo);
        AplicarSempreNoTopo();
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
        Width = 360; // o mesmo tamanho da primeira abertura (decisão 7)
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
