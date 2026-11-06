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
/// mensagens. Desligado no começo. O endereço é fixo; muda só com "Encontrar outro endereço" ou à mão, no Avançado.
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
    ];

    public static IReadOnlyList<OpcaoDaLista> Direcoes { get; } =
    [
        new("As novas embaixo", "bottom"),
        new("As novas em cima", "top"),
    ];

    public static IReadOnlyList<OpcaoDaLista> Alinhamentos { get; } =
    [
        new("À esquerda", "left"),
        new("À direita", "right"),
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
    [NotifyPropertyChangedFor(nameof(TextoDoEstado), nameof(TemProblema), nameof(Funcionando))]
    private bool _overlayLigado;

    /// <summary>Do jeito que foi digitada (Avançado); só um número de 1024 a 65535 vale.</summary>
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
                    0 => "Funcionando. Ainda nenhum OBS conectado: cole o endereço numa Fonte de navegador.",
                    1 => "Funcionando. O OBS está conectado.",
                    _ => $"Funcionando. {conectados} Fontes de navegador conectadas.",
                },
                EstadoDoOverlay.OutroOlhoNoChat =>
                    "Outro Olho no Chat aberto já está usando este endereço. Feche o outro (perto do relógio, clique com o " +
                    "botão direito no ícone do olho e em Sair) e o overlay volta sozinho, ou use outro endereço.",
                EstadoDoOverlay.PortaOcupada =>
                    "Outro programa está usando o endereço do overlay. O app tenta de novo sozinho de tempos em tempos, " +
                    "ou você pode usar outro endereço.",
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
        ResultadoDaProcura = "Pronto: o overlay está no endereço novo. Agora troque no OBS:";
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

    public string TextoDoAtraso => SegundosDeAtraso < 0.5 ? "Na hora" : Segundos(SegundosDeAtraso);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDasMensagens))]
    private double _mensagens = Opcoes.MensagensNoOverlayPadrao;

    public string TextoDasMensagens => Math.Round(Mensagens).ToString(CultureInfo.InvariantCulture);

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
    [NotifyPropertyChangedFor(nameof(ComCartao))]
    private string _fundo = "none";

    public bool ComCartao => Fundo == "card";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaOpacidade))]
    private double _opacidadeDoFundo = 55;

    public string TextoDaOpacidade => $"{Math.Round(OpacidadeDoFundo):0}%";

    [ObservableProperty]
    private string _direcao = "bottom";

    [ObservableProperty]
    private string _alinhamento = "left";

    [ObservableProperty]
    private string _animacao = "slide";

    [ObservableProperty]
    private bool _selos;

    [ObservableProperty]
    private bool _horario;

    [ObservableProperty]
    private bool _plataforma;

    // --- Só na live -------------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _esconderComandos;

    [ObservableProperty]
    private string _prefixoDosComandos = "!";

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

    private static string Segundos(double valor)
    {
        int segundos = (int)Math.Round(valor);
        if (segundos < 60)
            return segundos == 1 ? "1 segundo" : $"{segundos} segundos";
        int minutos = segundos / 60, resto = segundos % 60;
        return resto == 0 ? (minutos == 1 ? "1 minuto" : $"{minutos} minutos") : $"{minutos} min {resto} s";
    }

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
        Mensagens = opcoes.MensagensNoOverlay;
        TamanhoDoTexto = opcoes.TamanhoDoTextoNoOverlay;
        Fonte = opcoes.FonteDoOverlay;
        Negrito = opcoes.NegritoNoOverlay;
        Contorno = opcoes.ContornoNoOverlay;
        Fundo = opcoes.FundoNoOverlay;
        OpacidadeDoFundo = opcoes.OpacidadeDoFundoNoOverlay;
        Direcao = opcoes.DirecaoDoOverlay;
        Alinhamento = opcoes.AlinhamentoDoOverlay;
        Animacao = opcoes.AnimacaoDoOverlay;
        Selos = opcoes.SelosNoOverlay;
        Horario = opcoes.HorarioNoOverlay;
        Plataforma = opcoes.PlataformaNoOverlay;
        EsconderComandos = opcoes.EsconderComandosNoOverlay;
        PrefixoDosComandos = opcoes.PrefixoDosComandosNoOverlay;
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
        opcoes.MensagensNoOverlay = Inteiro(Mensagens, Opcoes.MensagensNoOverlayMinimo, Opcoes.MensagensNoOverlayMaximo);
        opcoes.TamanhoDoTextoNoOverlay = Inteiro(TamanhoDoTexto, Opcoes.TamanhoDoTextoNoOverlayMinimo, Opcoes.TamanhoDoTextoNoOverlayMaximo);
        opcoes.FonteDoOverlay = Fonte;
        opcoes.NegritoNoOverlay = Negrito;
        opcoes.ContornoNoOverlay = Contorno;
        opcoes.FundoNoOverlay = Fundo;
        opcoes.OpacidadeDoFundoNoOverlay = Inteiro(OpacidadeDoFundo, 0, 100);
        opcoes.DirecaoDoOverlay = Direcao;
        opcoes.AlinhamentoDoOverlay = Alinhamento;
        opcoes.AnimacaoDoOverlay = Animacao;
        opcoes.SelosNoOverlay = Selos;
        opcoes.HorarioNoOverlay = Horario;
        opcoes.PlataformaNoOverlay = Plataforma;
        opcoes.EsconderComandosNoOverlay = EsconderComandos;
        // Um prefixo vazio esconderia todas as mensagens: fica o de antes
        if (!string.IsNullOrWhiteSpace(PrefixoDosComandos))
            opcoes.PrefixoDosComandosNoOverlay = PrefixoDosComandos.Trim();
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
        // O que não valeu (uma porta inválida, o prefixo vazio) volta na tela para o que ficou gravado
        Porta = _portaSalva.ToString(CultureInfo.InvariantCulture);
        PrefixoDosComandos = opcoes.PrefixoDosComandosNoOverlay;
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
        estado["Overlay.Mensagens"] = TextoDasMensagens;
        estado["Overlay.TamanhoDoTexto"] = TextoDoTamanho;
        estado["Overlay.Fonte"] = Fonte;
        estado["Overlay.Negrito"] = Negrito.ToString();
        estado["Overlay.Contorno"] = Contorno;
        estado["Overlay.Fundo"] = Fundo;
        estado["Overlay.OpacidadeDoFundo"] = TextoDaOpacidade;
        estado["Overlay.Direcao"] = Direcao;
        estado["Overlay.Alinhamento"] = Alinhamento;
        estado["Overlay.Animacao"] = Animacao;
        estado["Overlay.Selos"] = Selos.ToString();
        estado["Overlay.Horario"] = Horario.ToString();
        estado["Overlay.Plataforma"] = Plataforma.ToString();
        estado["Overlay.EsconderComandos"] = EsconderComandos.ToString();
        estado["Overlay.PrefixoDosComandos"] = PrefixoDosComandos.Trim();
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
