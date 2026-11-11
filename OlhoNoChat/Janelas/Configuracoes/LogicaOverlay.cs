using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// O que a aba Overlay faz na hora (o resto espera o Salvar): dizer como o overlay está, achar outro endereço quando a
/// porta está ocupada, limpar o overlay e mandar as mensagens de teste. Vem da janela do chat, que cuida do servidor.
/// </summary>
/// <param name="Estado">Como o overlay está agora.</param>
/// <param name="Conectados">Quantas Fontes de navegador estão conectadas.</param>
/// <param name="Limpar">Tira tudo do overlay.</param>
/// <param name="MandarTeste">As mensagens de exemplo.</param>
/// <param name="EncontrarOutraPorta">Acha uma porta livre, grava e abre o overlay nela; null se nenhuma abrir.</param>
public sealed record AcoesDoOverlay(Func<EstadoDoOverlay> Estado, Func<int> Conectados, Action Limpar, Action MandarTeste,
    Func<int?> EncontrarOutraPorta);

/// <summary>
/// A aba Overlay: o overlay da live (uma Fonte de navegador do OBS com só as mensagens do chat) e como ele mostra as
/// mensagens. Desligado no começo. O endereço é fixo; muda só com "Encontrar outro endereço" ou à mão, em "Porta do
/// endereço".
/// </summary>
public sealed partial class LogicaOverlay : LogicaDaPagina
{
    private readonly AcoesDoOverlay? _acoes;
    private bool _ligadoSalvo;
    private int _portaSalva = ServidorDoOverlay.PortaPadrao;

    public LogicaOverlay(AcoesDoOverlay? acoes = null)
    {
        _acoes = acoes;
    }

    /// <summary>"Encontrar outro endereço" já gravou a porta nova (a janela não conta como mudança).</summary>
    public event Action? PortaGravada;

    public static IReadOnlyList<OpcaoDaLista> Fontes { get; } =
        ContratoDoOverlay.Fontes.Select(fonte => new OpcaoDaLista(fonte, fonte, Fonte: fonte)).ToList();

    public static IReadOnlyList<OpcaoDaLista> Contornos { get; } = LogicaAparencia.Contornos;

    public static IReadOnlyList<OpcaoDaLista> Fundos { get; } =
    [
        new("Sem fundo", "none"),
        new("Cartão escuro", "card"),
        new("Balão", "bubble"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Disposicoes { get; } =
    [
        new("Uma embaixo da outra", "list"),
        new("Faixa horizontal", "ticker"),
    ];

    private const string QuantasCouberem = "couberem";
    private const string AteUmNumero = "numero";

    /// <summary>A lista "Quantas mensagens" (só da tela: o que fica gravado é o <see cref="PreencherArea"/>).</summary>
    public static IReadOnlyList<OpcaoDaLista> Quantidades { get; } =
    [
        new("As que couberem", QuantasCouberem),
        new("Até um número", AteUmNumero),
    ];

    public static IReadOnlyList<OpcaoDaLista> Direcoes { get; } =
    [
        new("Embaixo", "bottom"),
        new("Em cima", "top"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Alinhamentos { get; } =
    [
        new("À esquerda", "left"),
        new("À direita", "right"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Perspectivas { get; } =
    [
        new("Desligada", "none"),
        new("Esquerda para dentro", "left"),
        new("Direita para dentro", "right"),
    ];

    public static IReadOnlyList<OpcaoDaLista> FormatosDaPerspectiva { get; } =
    [
        new("Funil", "funnel"),
        new("Inclinada", "tilt"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Animacoes { get; } =
    [
        new("Deslizar", "slide"),
        new("Aparecer suave", "fade"),
        new("Subir", "rise"),
        new("Pular", "pop"),
        new("Quicar", "bounce"),
        new("Desfocar", "blur"),
        new("Virar", "flip"),
        new("Sem animação", "none"),
    ];

    public static IReadOnlyList<OpcaoDaLista> OpcoesDeLinks { get; } =
    [
        new("Trocar por [link]", "replace"),
        new("Mostrar", "show"),
        new("Esconder a mensagem", "hide"),
    ];

    // --- O overlay ---------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoEstado), nameof(TemProblema), nameof(Funcionando), nameof(FaltaParaUsar))]
    private bool _overlayLigado;

    /// <summary>Do jeito que foi digitada ("Porta do endereço"); só um número de 1024 a 65535 vale.</summary>
    [ObservableProperty]
    private string _porta = string.Empty;

    /// <summary>Depois de "Encontrar outro endereço": como trocar o endereço no OBS.</summary>
    [ObservableProperty]
    private bool _mostrarPassosDoObs;

    [ObservableProperty]
    private string _resultadoDaProcura = string.Empty;

    /// <summary>O endereço que está valendo (o da porta salva).</summary>
    public string Endereco => ServidorDoOverlay.EnderecoDa(_portaSalva);

    private EstadoDoOverlay EstadoAgora => _acoes?.Estado() ?? EstadoDoOverlay.Desligado;

    public bool Funcionando => _ligadoSalvo && OverlayLigado && EstadoAgora == EstadoDoOverlay.Funcionando;

    public bool TemProblema => _ligadoSalvo && OverlayLigado && EstadoAgora is EstadoDoOverlay.PortaOcupada or EstadoDoOverlay.OutroOlhoNoChat;

    /// <summary>
    /// A dica de "Mensagens de teste", "Limpar" e do abrir no navegador enquanto estão desligados: o que falta para
    /// usar. Null quando o overlay está funcionando.
    /// </summary>
    public string? FaltaParaUsar
    {
        get
        {
            if (Funcionando)
                return null;
            if (!OverlayLigado)
                return _ligadoSalvo ? "Ligue o overlay de novo." : "Ligue o overlay e clique em Salvar.";
            if (!_ligadoSalvo)
                return "Clique em Salvar para ligar o overlay.";
            return TemProblema ? "O endereço está ocupado. Veja como resolver acima." : "O overlay está abrindo. Espere um pouco.";
        }
    }

    public string TextoDoEstado
    {
        get
        {
            if (!OverlayLigado)
                return _ligadoSalvo ? "Clique em Salvar para desligar." : "Desligado.";
            if (!_ligadoSalvo)
                return "Clique em Salvar para ligar.";
            int conectados = _acoes?.Conectados() ?? 0;
            return EstadoAgora switch
            {
                EstadoDoOverlay.Funcionando => conectados switch
                {
                    0 => "Funcionando, mas nenhum OBS está conectado. Cole o endereço numa Fonte de navegador do OBS.",
                    1 => "Funcionando. O OBS está conectado.",
                    _ => $"Funcionando. {conectados} Fontes de navegador conectadas.",
                },
                EstadoDoOverlay.OutroOlhoNoChat =>
                    "Outro Olho no Chat aberto está usando este endereço. Feche o outro (clique com o botão direito no ícone do olho, " +
                    "perto do relógio, e em Sair) e o overlay volta sozinho. Ou clique em \"Encontrar outro endereço\".",
                EstadoDoOverlay.PortaOcupada =>
                    "Outro programa está usando o endereço do overlay. O app tenta de novo sozinho de tempos em tempos; " +
                    "para resolver agora, clique em \"Encontrar outro endereço\".",
                _ => "Abrindo…",
            };
        }
    }

    /// <summary>A janela do chat avisa que o overlay mudou (abriu, a porta ficou ocupada, o OBS conectou).</summary>
    public void AtualizarEstado()
    {
        OnPropertyChanged(nameof(TextoDoEstado));
        OnPropertyChanged(nameof(TemProblema));
        OnPropertyChanged(nameof(Funcionando));
        OnPropertyChanged(nameof(FaltaParaUsar));
    }

    [RelayCommand]
    private void EncontrarOutroEndereco()
    {
        int? nova = _acoes?.EncontrarOutraPorta();
        if (nova == null)
        {
            ResultadoDaProcura = "Nenhum endereço livre agora. Feche outros programas de live (ou reinicie o PC) e tente de novo.";
            return;
        }
        _portaSalva = nova.Value;
        Porta = nova.Value.ToString(CultureInfo.InvariantCulture);
        ResultadoDaProcura = "Pronto, o overlay mudou de endereço. Agora troque no OBS:";
        MostrarPassosDoObs = true;
        OnPropertyChanged(nameof(Endereco));
        AtualizarEstado();
        PortaGravada?.Invoke();
    }

    [RelayCommand]
    private void Limpar() => _acoes?.Limpar();

    /// <summary>O atalho que limpa o overlay na hora (null = sem atalho). Vale só com o overlay ligado.</summary>
    [ObservableProperty]
    private Atalho? _atalhoLimpar;

    [RelayCommand]
    private void MandarTeste() => _acoes?.MandarTeste();

    // --- Como as mensagens aparecem ---------------------------------------------------------------------------

    [ObservableProperty]
    private bool _overlaySome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoSumir))]
    private double _segundosParaSumir = Opcoes.SegundosParaSumirNoOverlayPadrao;

    public string TextoDoSumir => Segundos(SegundosParaSumir);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoAtraso))]
    private double _segundosDeAtraso;

    public string TextoDoAtraso => SegundosDeAtraso < 0.5 ? "Sem atraso" : Segundos(SegundosDeAtraso);

    /// <summary>Verdadeiro: as que couberem na fonte do OBS; falso: no máximo <see cref="Mensagens"/>. É o que fica gravado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Quantidade))]
    [NotifyPropertyChangedFor(nameof(AteUmNumeroEscolhido))]
    private bool _preencherArea;

    /// <summary>"No máximo" só aparece com "Até um número" (com "As que couberem" ele não vale).</summary>
    public bool AteUmNumeroEscolhido => !PreencherArea;

    /// <summary>"Quantas mensagens" na tela: "As que couberem" ou "Até um número" (o <see cref="PreencherArea"/>).</summary>
    public string Quantidade
    {
        get => PreencherArea ? QuantasCouberem : AteUmNumero;
        set
        {
            // Só um dos dois da lista muda alguma coisa
            if (value is QuantasCouberem or AteUmNumero)
                PreencherArea = value == QuantasCouberem;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDasMensagens))]
    private double _mensagens = Opcoes.MensagensNoOverlayPadrao;

    public string TextoDasMensagens => Math.Round(Mensagens) == 1 ? "1 mensagem" : $"{Math.Round(Mensagens):0} mensagens";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoTamanho))]
    private double _tamanhoDoTexto = Opcoes.TamanhoDoTextoNoOverlayPadrao;

    public string TextoDoTamanho => $"{Math.Round(TamanhoDoTexto):0} px";

    [ObservableProperty]
    private string _fonte = ContratoDoOverlay.Fontes[0];

    [ObservableProperty]
    private bool _negrito;

    [ObservableProperty]
    private string _contorno = "theme";

    [ObservableProperty]
    private string _fundo = "none";

    /// <summary>"Uma embaixo da outra" ou "Faixa horizontal" (passando da direita para a esquerda).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmLista), nameof(EmFaixa))]
    private string _disposicao = "list";

    /// <summary>Quantas, somem, onde entram, alinhamento e animação só valem (e só aparecem) uma embaixo da outra.</summary>
    public bool EmLista => Disposicao != "ticker";

    public bool EmFaixa => Disposicao == "ticker";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaVelocidadeDaFaixa))]
    private double _velocidadeDaFaixa = Opcoes.VelocidadeDaFaixaPadrao;

    public string TextoDaVelocidadeDaFaixa => Math.Round(VelocidadeDaFaixa) switch
    {
        < 80 => "Devagar",
        < 180 => "Normal",
        < 280 => "Rápida",
        _ => "Muito rápida",
    } + $" ({Math.Round(VelocidadeDaFaixa):0})";

    [ObservableProperty]
    private bool _primeiraVez;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaOpacidade))]
    private double _opacidadeDoFundo = 55;

    public string TextoDaOpacidade => $"{Math.Round(OpacidadeDoFundo):0}%";

    [ObservableProperty]
    private string _direcao = "bottom";

    [ObservableProperty]
    private string _alinhamento = "left";

    [ObservableProperty]
    private bool _saidaSuave = true;

    [ObservableProperty]
    private string _perspectiva = "none";

    [ObservableProperty]
    private string _formatoDaPerspectiva = "funnel";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoCone))]
    private double _cone = Opcoes.ConeDoFunilPadrao;

    public string TextoDoCone => Math.Round(Cone) switch
    {
        <= 3 => "Aberto",
        <= 7 => "Médio",
        _ => "Fechado",
    } + $" ({Math.Round(Cone):0})";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaInclinacao))]
    private double _inclinacao = Opcoes.InclinacaoNoOverlayPadrao;

    public string TextoDaInclinacao => $"{Math.Round(Inclinacao):0}°";

    [ObservableProperty]
    private string _animacao = "pop";

    [ObservableProperty]
    private bool _selos;

    [ObservableProperty]
    private bool _horario;

    [ObservableProperty]
    private bool _plataforma;

    // --- Só na live -------------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _esconderComandos;

    /// <summary>
    /// Os prefixos dos comandos da aba Chat (os da caixa de lá, mesmo antes de salvar), que o "Esconder comandos" usa.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DicaDosComandos))]
    private IReadOnlyList<string> _prefixosDoChat = ["!"];

    /// <summary>"Esconde as mensagens que começam com ! ou $, como !discord. Os prefixos ficam na aba Chat."</summary>
    public string DicaDosComandos
    {
        get
        {
            IReadOnlyList<string> p = PrefixosDoChat.Count > 0 ? PrefixosDoChat : ["!"];
            string lista = p.Count == 1 ? p[0] : string.Join(", ", p.Take(p.Count - 1)) + " ou " + p[^1];
            return $"Esconde as mensagens que começam com {lista}, como {p[0]}discord. Os prefixos ficam na aba Chat.";
        }
    }

    [ObservableProperty]
    private bool _esconderBots;

    /// <summary>Uma por linha (ou separadas por vírgula).</summary>
    [ObservableProperty]
    private string _palavrasProibidas = string.Empty;

    [ObservableProperty]
    private string _links = "replace";

    [ObservableProperty]
    private bool _limitarTamanho;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDosCaracteres))]
    private double _caracteres = Opcoes.CaracteresNoOverlayPadrao;

    public string TextoDosCaracteres => $"{Math.Round(Caracteres):0} letras";

    [ObservableProperty]
    private bool _limitarEmotes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDosEmotes))]
    private double _emotes = Opcoes.EmotesNoOverlayPadrao;

    public string TextoDosEmotes => Math.Round(Emotes) == 1 ? "1 emote" : $"{Math.Round(Emotes):0} emotes";

    // Os avisos na live
    [ObservableProperty]
    private bool _inscricoes;

    [ObservableProperty]
    private bool _raids;

    [ObservableProperty]
    private bool _outrosAvisosDaTwitch;

    [ObservableProperty]
    private bool _resgates;

    [ObservableProperty]
    private bool _superChats;

    [ObservableProperty]
    private bool _membrosDoYouTube;

    [ObservableProperty]
    private bool _presentesDoTikTok;

    [ObservableProperty]
    private bool _outrosAvisosDoTikTok;

    // O mesmo texto de tempo da aba Chat ("45 segundos", "1 min 30 s")
    private static string Segundos(double valor) => LogicaChat.TextoDoTempo((int)Math.Round(valor));

    /// <summary>As palavras da caixa: uma por linha ou separadas por vírgula, sem repetir (maiúsculas não contam).</summary>
    public static List<string> LerPalavras(string texto) =>
        texto.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public override void Carregar(Opcoes opcoes)
    {
        _ligadoSalvo = opcoes.OverlayLigado;
        _portaSalva = opcoes.PortaDoOverlay;
        OverlayLigado = opcoes.OverlayLigado;
        Porta = opcoes.PortaDoOverlay.ToString(CultureInfo.InvariantCulture);
        MostrarPassosDoObs = false;
        ResultadoDaProcura = string.Empty;
        AtalhoLimpar = opcoes.AtalhoLimparOverlay;
        OverlaySome = opcoes.OverlaySome;
        SegundosParaSumir = opcoes.SegundosParaSumirNoOverlay;
        SegundosDeAtraso = opcoes.SegundosDeAtrasoNoOverlay;
        PreencherArea = opcoes.PreencherAreaNoOverlay;
        Mensagens = opcoes.MensagensNoOverlay;
        TamanhoDoTexto = opcoes.TamanhoDoTextoNoOverlay;
        Fonte = opcoes.FonteDoOverlay;
        Negrito = opcoes.NegritoNoOverlay;
        Contorno = opcoes.ContornoNoOverlay;
        Fundo = opcoes.FundoNoOverlay;
        Disposicao = opcoes.DisposicaoDoOverlay;
        VelocidadeDaFaixa = opcoes.VelocidadeDaFaixaNoOverlay;
        PrimeiraVez = opcoes.PrimeiraVezNoOverlay;
        OpacidadeDoFundo = opcoes.OpacidadeDoFundoNoOverlay;
        Direcao = opcoes.DirecaoDoOverlay;
        Alinhamento = opcoes.AlinhamentoDoOverlay;
        SaidaSuave = opcoes.SaidaSuaveNoOverlay;
        Perspectiva = opcoes.PerspectivaNoOverlay;
        FormatoDaPerspectiva = opcoes.FormatoDaPerspectivaNoOverlay;
        Cone = opcoes.ConeDoFunilNoOverlay;
        Inclinacao = opcoes.InclinacaoNoOverlay;
        Animacao = opcoes.AnimacaoDoOverlay;
        Selos = opcoes.SelosNoOverlay;
        Horario = opcoes.HorarioNoOverlay;
        Plataforma = opcoes.PlataformaNoOverlay;
        EsconderComandos = opcoes.EsconderComandosNoOverlay;
        EsconderBots = opcoes.EsconderBotsNoOverlay;
        PalavrasProibidas = string.Join(Environment.NewLine, opcoes.PalavrasProibidasNoOverlay);
        Links = opcoes.LinksNoOverlay;
        LimitarTamanho = opcoes.LimitarTamanhoNoOverlay;
        Caracteres = opcoes.CaracteresNoOverlay;
        LimitarEmotes = opcoes.LimitarEmotesNoOverlay;
        Emotes = opcoes.EmotesNoOverlay;
        Inscricoes = opcoes.InscricoesNoOverlay;
        Raids = opcoes.RaidsNoOverlay;
        OutrosAvisosDaTwitch = opcoes.OutrosAvisosDaTwitchNoOverlay;
        Resgates = opcoes.ResgatesNoOverlay;
        SuperChats = opcoes.SuperChatsNoOverlay;
        MembrosDoYouTube = opcoes.MembrosDoYouTubeNoOverlay;
        PresentesDoTikTok = opcoes.PresentesDoTikTokNoOverlay;
        OutrosAvisosDoTikTok = opcoes.OutrosAvisosDoTikTokNoOverlay;
        OnPropertyChanged(nameof(Endereco));
        AtualizarEstado();
    }

    public override void Gravar(Opcoes opcoes)
    {
        opcoes.OverlayLigado = OverlayLigado;
        if (int.TryParse(Porta.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int porta) && ServidorDoOverlay.PortaValida(porta))
            opcoes.PortaDoOverlay = porta;
        opcoes.AtalhoLimparOverlay = Atalho.Existe(AtalhoLimpar) ? AtalhoLimpar : null;
        opcoes.OverlaySome = OverlaySome;
        opcoes.SegundosParaSumirNoOverlay = Inteiro(SegundosParaSumir, Opcoes.SegundosParaSumirNoOverlayMinimo, Opcoes.SegundosParaSumirNoOverlayMaximo);
        opcoes.SegundosDeAtrasoNoOverlay = Inteiro(SegundosDeAtraso, 0, Opcoes.SegundosDeAtrasoNoOverlayMaximo);
        opcoes.PreencherAreaNoOverlay = PreencherArea;
        opcoes.MensagensNoOverlay = Inteiro(Mensagens, Opcoes.MensagensNoOverlayMinimo, Opcoes.MensagensNoOverlayMaximo);
        opcoes.TamanhoDoTextoNoOverlay = Inteiro(TamanhoDoTexto, Opcoes.TamanhoDoTextoNoOverlayMinimo, Opcoes.TamanhoDoTextoNoOverlayMaximo);
        opcoes.FonteDoOverlay = Fonte;
        opcoes.NegritoNoOverlay = Negrito;
        opcoes.ContornoNoOverlay = Contorno;
        opcoes.FundoNoOverlay = Fundo;
        opcoes.DisposicaoDoOverlay = Disposicao;
        opcoes.VelocidadeDaFaixaNoOverlay = Inteiro(VelocidadeDaFaixa, Opcoes.VelocidadeDaFaixaMinima, Opcoes.VelocidadeDaFaixaMaxima);
        opcoes.PrimeiraVezNoOverlay = PrimeiraVez;
        opcoes.OpacidadeDoFundoNoOverlay = Inteiro(OpacidadeDoFundo, 0, 100);
        opcoes.DirecaoDoOverlay = Direcao;
        opcoes.AlinhamentoDoOverlay = Alinhamento;
        opcoes.SaidaSuaveNoOverlay = SaidaSuave;
        opcoes.PerspectivaNoOverlay = Perspectiva;
        opcoes.FormatoDaPerspectivaNoOverlay = FormatoDaPerspectiva;
        opcoes.ConeDoFunilNoOverlay = Inteiro(Cone, Opcoes.ConeDoFunilMinimo, Opcoes.ConeDoFunilMaximo);
        opcoes.InclinacaoNoOverlay = Inteiro(Inclinacao, Opcoes.InclinacaoNoOverlayMinima, Opcoes.InclinacaoNoOverlayMaxima);
        opcoes.AnimacaoDoOverlay = Animacao;
        opcoes.SelosNoOverlay = Selos;
        opcoes.HorarioNoOverlay = Horario;
        opcoes.PlataformaNoOverlay = Plataforma;
        opcoes.EsconderComandosNoOverlay = EsconderComandos;
        opcoes.EsconderBotsNoOverlay = EsconderBots;
        opcoes.PalavrasProibidasNoOverlay = LerPalavras(PalavrasProibidas);
        opcoes.LinksNoOverlay = Links;
        opcoes.LimitarTamanhoNoOverlay = LimitarTamanho;
        opcoes.CaracteresNoOverlay = Inteiro(Caracteres, Opcoes.CaracteresNoOverlayMinimo, Opcoes.CaracteresNoOverlayMaximo);
        opcoes.LimitarEmotesNoOverlay = LimitarEmotes;
        opcoes.EmotesNoOverlay = Inteiro(Emotes, Opcoes.EmotesNoOverlayMinimo, Opcoes.EmotesNoOverlayMaximo);
        opcoes.InscricoesNoOverlay = Inscricoes;
        opcoes.RaidsNoOverlay = Raids;
        opcoes.OutrosAvisosDaTwitchNoOverlay = OutrosAvisosDaTwitch;
        opcoes.ResgatesNoOverlay = Resgates;
        opcoes.SuperChatsNoOverlay = SuperChats;
        opcoes.MembrosDoYouTubeNoOverlay = MembrosDoYouTube;
        opcoes.PresentesDoTikTokNoOverlay = PresentesDoTikTok;
        opcoes.OutrosAvisosDoTikTokNoOverlay = OutrosAvisosDoTikTok;

        _ligadoSalvo = opcoes.OverlayLigado;
        _portaSalva = opcoes.PortaDoOverlay;
        // Uma porta que não valeu volta na tela para a que ficou gravada
        Porta = _portaSalva.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Endereco));
        AtualizarEstado();
    }

    private static int Inteiro(double valor, int minimo, int maximo) =>
        double.IsNaN(valor) ? minimo : Math.Clamp((int)Math.Round(valor), minimo, maximo);

    /// <summary>A chave da porta no estado da tela (o "Encontrar outro endereço" grava sem contar como mudança).</summary>
    public const string ChavePorta = "Overlay.Porta";

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Overlay.Ligado"] = OverlayLigado.ToString();
        estado[ChavePorta] = Porta.Trim();
        estado["Overlay.AtalhoLimpar"] = Atalho.TextoOuNenhum(AtalhoLimpar);
        estado["Overlay.Some"] = OverlaySome.ToString();
        estado["Overlay.SegundosParaSumir"] = TextoDoSumir;
        estado["Overlay.SegundosDeAtraso"] = TextoDoAtraso;
        estado["Overlay.PreencherArea"] = PreencherArea.ToString();
        estado["Overlay.Mensagens"] = TextoDasMensagens;
        estado["Overlay.TamanhoDoTexto"] = TextoDoTamanho;
        estado["Overlay.Fonte"] = Fonte;
        estado["Overlay.Negrito"] = Negrito.ToString();
        estado["Overlay.Contorno"] = Contorno;
        estado["Overlay.Fundo"] = Fundo;
        estado["Overlay.Disposicao"] = Disposicao;
        estado["Overlay.VelocidadeDaFaixa"] = TextoDaVelocidadeDaFaixa;
        estado["Overlay.PrimeiraVez"] = PrimeiraVez.ToString();
        estado["Overlay.OpacidadeDoFundo"] = TextoDaOpacidade;
        estado["Overlay.Direcao"] = Direcao;
        estado["Overlay.Alinhamento"] = Alinhamento;
        estado["Overlay.SaidaSuave"] = SaidaSuave.ToString();
        estado["Overlay.Perspectiva"] = Perspectiva;
        estado["Overlay.FormatoDaPerspectiva"] = FormatoDaPerspectiva;
        estado["Overlay.Cone"] = TextoDoCone;
        estado["Overlay.Inclinacao"] = TextoDaInclinacao;
        estado["Overlay.Animacao"] = Animacao;
        estado["Overlay.Selos"] = Selos.ToString();
        estado["Overlay.Horario"] = Horario.ToString();
        estado["Overlay.Plataforma"] = Plataforma.ToString();
        estado["Overlay.EsconderComandos"] = EsconderComandos.ToString();
        estado["Overlay.EsconderBots"] = EsconderBots.ToString();
        estado["Overlay.PalavrasProibidas"] = string.Join("\n", LerPalavras(PalavrasProibidas));
        estado["Overlay.Links"] = Links;
        estado["Overlay.LimitarTamanho"] = LimitarTamanho.ToString();
        estado["Overlay.Caracteres"] = TextoDosCaracteres;
        estado["Overlay.LimitarEmotes"] = LimitarEmotes.ToString();
        estado["Overlay.Emotes"] = TextoDosEmotes;
        estado["Overlay.Inscricoes"] = Inscricoes.ToString();
        estado["Overlay.Raids"] = Raids.ToString();
        estado["Overlay.OutrosAvisosDaTwitch"] = OutrosAvisosDaTwitch.ToString();
        estado["Overlay.Resgates"] = Resgates.ToString();
        estado["Overlay.SuperChats"] = SuperChats.ToString();
        estado["Overlay.MembrosDoYouTube"] = MembrosDoYouTube.ToString();
        estado["Overlay.PresentesDoTikTok"] = PresentesDoTikTok.ToString();
        estado["Overlay.OutrosAvisosDoTikTok"] = OutrosAvisosDoTikTok.ToString();
    }
}
