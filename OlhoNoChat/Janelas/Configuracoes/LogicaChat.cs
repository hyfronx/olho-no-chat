using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Filtros;
using Tipos = OlhoNoChat.Chat.TipoDeChat;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Chat: o tipo de chat (que muda o que as abas Chat, Aparência e Som mostram) e as opções de cada tipo. O cartão
/// "Filtrar ou destacar usuários" abre a janela Filtros do chat, que salva sozinha.
/// </summary>
public sealed partial class LogicaChat : LogicaDaPagina
{
    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _gravar;

    /// <param name="opcoes">As opções em uso (a janela Filtros do chat lê e grava nelas).</param>
    /// <param name="gravar">Grava as opções no arquivo.</param>
    public LogicaChat(Func<Opcoes> opcoes, Func<bool> gravar)
    {
        _opcoes = opcoes;
        _gravar = gravar;
    }

    /// <summary>A janela Filtros do chat salvou: o chat aplica na hora.</summary>
    public event Action? FiltrosSalvos;

    /// <summary>A posição na lista "Tipo de chat" (0 Padrão, 1 Chat oficial da Twitch, 2 Endereço personalizado).</summary>
    [ObservableProperty]
    private int _tipoDeChat;

    /// <summary>Chat Multiplataforma (Twitch, YouTube, Kick e TikTok): só existe no Padrão, então ligar escolhe o Padrão e trava a lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeEscolherOTipo))]
    private bool _chatMultiplataforma;

    public bool PodeEscolherOTipo => !ChatMultiplataforma;

    /// <summary>Ao conectar no chat de uma live, mostrar também as mensagens de antes (desligado no começo).</summary>
    [ObservableProperty]
    private bool _mostrarHistoricoDoYouTube;

    /// <summary>Quantos assistem cada canal ao vivo, na faixa do canal (sem as bordas, no topo do Padrão).</summary>
    [ObservableProperty]
    private bool _mostrarViewers;

    partial void OnChatMultiplataformaChanged(bool value)
    {
        if (value)
            TipoDeChat = (int)Tipos.Padrao;
    }

    // Padrão
    [ObservableProperty]
    private bool _apagarMensagensAntigas;

    /// <summary>Os tempos do deslizante "Apagar depois de", em segundos: de 10 s a 10 min.</summary>
    public static IReadOnlyList<int> TemposParaApagar { get; } = [10, 15, 20, 30, 45, 60, 90, 120, 180, 300, 600];

    // O padrão das opções: um texto salvo que não é um número inteiro maior que 0 aparece como ele
    private static readonly int SegundosParaApagarPadrao = int.Parse(new Opcoes().SegundosParaApagar, CultureInfo.InvariantCulture);

    // Os tempos do deslizante: os da lista e o salvo, quando é outro (como em "Quando tocar", da aba Som)
    private readonly List<int> _temposNoDeslizante = [.. TemposParaApagar];

    /// <summary>
    /// "Apagar depois de", em segundos (gravado como texto em <see cref="Opcoes.SegundosParaApagar"/>). O deslizante só
    /// dá os <see cref="TemposParaApagar"/> e o salvo.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PosicaoParaApagar))]
    [NotifyPropertyChangedFor(nameof(TextoParaApagar))]
    private int _segundosParaApagar = SegundosParaApagarPadrao;

    /// <summary>A posição do deslizante "Apagar depois de" (de 0 a <see cref="UltimaPosicaoParaApagar"/>).</summary>
    public int PosicaoParaApagar
    {
        get => _temposNoDeslizante.IndexOf(SegundosParaApagar);
        set => SegundosParaApagar = _temposNoDeslizante[Math.Clamp(value, 0, UltimaPosicaoParaApagar)];
    }

    public int UltimaPosicaoParaApagar => _temposNoDeslizante.Count - 1;

    /// <summary>"45 segundos", "2 minutos".</summary>
    public string TextoParaApagar => TextoDoTempo(SegundosParaApagar);

    /// <summary>"1 segundo", "45 segundos", "1 minuto", "2 minutos", "1 min 30 s".</summary>
    public static string TextoDoTempo(int segundos)
    {
        if (segundos < 60)
            return segundos == 1 ? "1 segundo" : $"{segundos} segundos";
        int minutos = segundos / 60, resto = segundos % 60;
        return resto == 0 ? (minutos == 1 ? "1 minuto" : $"{minutos} minutos") : $"{minutos} min {resto} s";
    }

    // Um tempo que não está no deslizante entra nele, no lugar certo (o deslizante fica sabendo antes da posição nova)
    partial void OnSegundosParaApagarChanged(int value)
    {
        if (_temposNoDeslizante.Contains(value))
            return;
        _temposNoDeslizante.Add(value);
        _temposNoDeslizante.Sort();
        OnPropertyChanged(nameof(UltimaPosicaoParaApagar));
    }

    // A caixa mostra os prefixos salvos, separados por espaço
    private void MostrarPrefixosSalvos(Opcoes opcoes)
    {
        _prefixosSalvos = [.. opcoes.PrefixosDosComandos];
        PrefixosDosComandos = Opcoes.TextoDosPrefixos(_prefixosSalvos);
        OnPropertyChanged(nameof(PrefixosQueValem));
    }

    // Os segundos salvos; um texto que não é um número inteiro maior que 0 (que nunca apagaria) dá o padrão
    private static int LerSegundosParaApagar(string? texto) =>
        int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int segundos) && segundos > 0
            ? segundos
            : SegundosParaApagarPadrao;

    /// <summary>Continuar a pausa no ritmo de leitura (pela quantidade de texto); desligado, na velocidade fixa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VelocidadeFixa))]
    private bool _velocidadeAutomaticaAoContinuar;

    /// <summary>A velocidade fixa, em mensagens de uma linha por segundo (0,5 a 6, de 0,5 em 0,5 no deslizante).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaVelocidade))]
    private double _linhasPorSegundoAoContinuar;

    public bool VelocidadeFixa => !VelocidadeAutomaticaAoContinuar;

    /// <summary>"1,5 linhas/s", "1 linha/s".</summary>
    public string TextoDaVelocidade => LinhasPorSegundo(LinhasPorSegundoAoContinuar);

    public static string LinhasPorSegundo(double valor)
    {
        double redondo = Math.Round(valor, 1);
        return redondo.ToString("0.#", Pt) + (redondo < 2 ? " linha/s" : " linhas/s");
    }

    private static readonly CultureInfo Pt = new("pt-BR");

    [ObservableProperty]
    private bool _esconderBots;

    /// <summary>
    /// "Prefixos dos comandos", como a pessoa escreve ("! $" ou "!, $"); gravados como lista em
    /// <see cref="Opcoes.PrefixosDosComandos"/>. Valem também para o "Esconder comandos" do overlay.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrefixosQueValem))]
    private string _prefixosDosComandos = "!";

    // Os salvos, para a caixa vazia (que não grava nada)
    private List<string> _prefixosSalvos = ["!"];

    /// <summary>Os prefixos da caixa; com ela vazia, os salvos (é o que fica valendo ao salvar).</summary>
    public IReadOnlyList<string> PrefixosQueValem =>
        Opcoes.LerPrefixos(PrefixosDosComandos) is { Count: > 0 } prefixos ? prefixos : _prefixosSalvos;

    [ObservableProperty]
    private bool _esconderGifs;

    [ObservableProperty]
    private bool _mostrarPrimeiraVez;

    [ObservableProperty]
    private bool _esconderOutrosCanais;

    [ObservableProperty]
    private bool _mostrarAvisosDaTwitch;

    [ObservableProperty]
    private bool _mostrarAvisosDoYouTube;

    [ObservableProperty]
    private bool _mostrarPresentesDoTikTok;

    [ObservableProperty]
    private bool _mostrarSeguidoresDoTikTok;

    [ObservableProperty]
    private bool _mostrarCompartilhamentosDoTikTok;

    [ObservableProperty]
    private bool _mostrarSuperfasDoTikTok;

    // Chat oficial da Twitch
    [ObservableProperty]
    private bool _betterTtv;

    [ObservableProperty]
    private bool _menuDeEmotesDoBetterTtv;

    [ObservableProperty]
    private bool _emotes7tv;

    [ObservableProperty]
    private bool _frankerFaceZ;

    [ObservableProperty]
    private bool _esconderTituloDoChat;

    [ObservableProperty]
    private bool _esconderPlacarDoTopo;

    [ObservableProperty]
    private bool _esconderDestaques;

    // Endereço personalizado
    [ObservableProperty]
    private string _enderecoPersonalizado = string.Empty;

    /// <summary>O tipo escolhido na lista (um valor fora da lista conta como Padrão).</summary>
    public Tipos Tipo => TiposDeChat.Ler(TipoDeChat);

    public override void Carregar(Opcoes opcoes)
    {
        TipoDeChat = (int)TiposDeChat.Ler(opcoes.TipoDeChat);
        ChatMultiplataforma = opcoes.ChatMultiplataforma;
        MostrarHistoricoDoYouTube = opcoes.MostrarHistoricoDoYouTube;
        MostrarViewers = opcoes.MostrarViewers;
        ApagarMensagensAntigas = opcoes.ApagarMensagensAntigas;
        // O deslizante volta aos tempos da lista, mais o salvo quando é outro; o máximo muda antes da posição
        int segundos = LerSegundosParaApagar(opcoes.SegundosParaApagar);
        _temposNoDeslizante.Clear();
        _temposNoDeslizante.AddRange(TemposParaApagar.Append(segundos).Distinct().Order());
        OnPropertyChanged(nameof(UltimaPosicaoParaApagar));
        SegundosParaApagar = segundos;
        OnPropertyChanged(nameof(PosicaoParaApagar));
        VelocidadeAutomaticaAoContinuar = opcoes.VelocidadeAutomaticaAoContinuar;
        LinhasPorSegundoAoContinuar = opcoes.LinhasPorSegundoAoContinuar;
        EsconderBots = opcoes.EsconderBots;
        MostrarPrefixosSalvos(opcoes);
        EsconderGifs = opcoes.EsconderGifs;
        MostrarPrimeiraVez = opcoes.MostrarPrimeiraVez;
        EsconderOutrosCanais = opcoes.EsconderOutrosCanais;
        MostrarAvisosDaTwitch = opcoes.MostrarAvisosDaTwitch;
        MostrarAvisosDoYouTube = opcoes.MostrarAvisosDoYouTube;
        MostrarPresentesDoTikTok = opcoes.MostrarPresentesDoTikTok;
        MostrarSeguidoresDoTikTok = opcoes.MostrarSeguidoresDoTikTok;
        MostrarCompartilhamentosDoTikTok = opcoes.MostrarCompartilhamentosDoTikTok;
        MostrarSuperfasDoTikTok = opcoes.MostrarSuperfasDoTikTok;
        BetterTtv = opcoes.BetterTtv;
        MenuDeEmotesDoBetterTtv = opcoes.MenuDeEmotesDoBetterTtv;
        Emotes7tv = opcoes.Emotes7tv;
        FrankerFaceZ = opcoes.FrankerFaceZ;
        EsconderTituloDoChat = opcoes.EsconderTituloDoChat;
        EsconderPlacarDoTopo = opcoes.EsconderPlacarDoTopo;
        EsconderDestaques = opcoes.EsconderDestaques;
        // A caixa só mostra o endereço quando ele é o do tipo salvo
        EnderecoPersonalizado = opcoes.TipoDeChat == (int)Tipos.EnderecoPersonalizado ? opcoes.EnderecoPersonalizado : string.Empty;
    }

    public override void Gravar(Opcoes opcoes)
    {
        opcoes.ChatMultiplataforma = ChatMultiplataforma && TipoNaTela == Tipos.Padrao;
        opcoes.MostrarHistoricoDoYouTube = MostrarHistoricoDoYouTube;
        opcoes.MostrarViewers = MostrarViewers;
        switch (TipoNaTela)
        {
            case Tipos.Padrao:
                opcoes.EnderecoPersonalizado = string.Empty;
                opcoes.ApagarMensagensAntigas = ApagarMensagensAntigas;
                opcoes.SegundosParaApagar = SegundosParaApagar.ToString(CultureInfo.InvariantCulture);
                opcoes.VelocidadeAutomaticaAoContinuar = VelocidadeAutomaticaAoContinuar;
                opcoes.LinhasPorSegundoAoContinuar = Math.Clamp(Math.Round(LinhasPorSegundoAoContinuar * 2) / 2,
                    Opcoes.LinhasPorSegundoMinimo, Opcoes.LinhasPorSegundoMaximo);
                opcoes.EsconderBots = EsconderBots;
                // A caixa vazia não grava (não sobraria prefixo nenhum): volta a mostrar os salvos
                opcoes.PrefixosDosComandos = [.. PrefixosQueValem];
                MostrarPrefixosSalvos(opcoes);
                opcoes.EsconderGifs = EsconderGifs;
                opcoes.MostrarPrimeiraVez = MostrarPrimeiraVez;
                opcoes.EsconderOutrosCanais = EsconderOutrosCanais;
                opcoes.MostrarAvisosDaTwitch = MostrarAvisosDaTwitch;
                opcoes.MostrarAvisosDoYouTube = MostrarAvisosDoYouTube;
                opcoes.MostrarPresentesDoTikTok = MostrarPresentesDoTikTok;
                opcoes.MostrarSeguidoresDoTikTok = MostrarSeguidoresDoTikTok;
                opcoes.MostrarCompartilhamentosDoTikTok = MostrarCompartilhamentosDoTikTok;
                opcoes.MostrarSuperfasDoTikTok = MostrarSuperfasDoTikTok;
                break;
            case Tipos.ChatOficial:
                opcoes.BetterTtv = BetterTtv;
                opcoes.MenuDeEmotesDoBetterTtv = MenuDeEmotesDoBetterTtv;
                opcoes.Emotes7tv = Emotes7tv;
                opcoes.FrankerFaceZ = FrankerFaceZ;
                opcoes.EsconderTituloDoChat = EsconderTituloDoChat;
                opcoes.EsconderPlacarDoTopo = EsconderPlacarDoTopo;
                opcoes.EsconderDestaques = EsconderDestaques;
                break;
            case Tipos.EnderecoPersonalizado:
                opcoes.EnderecoPersonalizado = EnderecoPersonalizado;
                break;
        }
    }

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Chat.TipoDeChat"] = TipoDeChat.ToString();
        estado["Chat.ChatMultiplataforma"] = ChatMultiplataforma.ToString();
        estado["Chat.MostrarHistoricoDoYouTube"] = MostrarHistoricoDoYouTube.ToString();
        estado["Chat.MostrarViewers"] = MostrarViewers.ToString();
        estado["Chat.ApagarMensagensAntigas"] = ApagarMensagensAntigas.ToString();
        estado["Chat.SegundosParaApagar"] = SegundosParaApagar.ToString(CultureInfo.InvariantCulture);
        estado["Chat.VelocidadeAutomaticaAoContinuar"] = VelocidadeAutomaticaAoContinuar.ToString();
        estado["Chat.LinhasPorSegundoAoContinuar"] = TextoDaVelocidade;
        estado["Chat.EsconderBots"] = EsconderBots.ToString();
        // "!, $" e "! $" são a mesma coisa
        estado["Chat.PrefixosDosComandos"] = Opcoes.TextoDosPrefixos(PrefixosQueValem);
        estado["Chat.EsconderGifs"] = EsconderGifs.ToString();
        estado["Chat.MostrarPrimeiraVez"] = MostrarPrimeiraVez.ToString();
        estado["Chat.EsconderOutrosCanais"] = EsconderOutrosCanais.ToString();
        estado["Chat.MostrarAvisosDaTwitch"] = MostrarAvisosDaTwitch.ToString();
        estado["Chat.MostrarAvisosDoYouTube"] = MostrarAvisosDoYouTube.ToString();
        estado["Chat.MostrarPresentesDoTikTok"] = MostrarPresentesDoTikTok.ToString();
        estado["Chat.MostrarSeguidoresDoTikTok"] = MostrarSeguidoresDoTikTok.ToString();
        estado["Chat.MostrarCompartilhamentosDoTikTok"] = MostrarCompartilhamentosDoTikTok.ToString();
        estado["Chat.MostrarSuperfasDoTikTok"] = MostrarSuperfasDoTikTok.ToString();
        estado["Chat.BetterTtv"] = BetterTtv.ToString();
        estado["Chat.MenuDeEmotesDoBetterTtv"] = MenuDeEmotesDoBetterTtv.ToString();
        estado["Chat.Emotes7tv"] = Emotes7tv.ToString();
        estado["Chat.FrankerFaceZ"] = FrankerFaceZ.ToString();
        estado["Chat.EsconderTituloDoChat"] = EsconderTituloDoChat.ToString();
        estado["Chat.EsconderPlacarDoTopo"] = EsconderPlacarDoTopo.ToString();
        estado["Chat.EsconderDestaques"] = EsconderDestaques.ToString();
        estado["Chat.EnderecoPersonalizado"] = EnderecoPersonalizado;
    }

    /// <summary>A lógica da janela Filtros do chat, que lê e grava direto nas opções (não depende do "Salvar" daqui).</summary>
    public LogicaFiltros NovaLogicaDosFiltros()
    {
        var filtros = new LogicaFiltros(_opcoes(), _gravar);
        filtros.Salvou += () => FiltrosSalvos?.Invoke();
        return filtros;
    }
}
