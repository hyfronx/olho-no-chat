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

    /// <summary>Chat Multiplataforma (Twitch, YouTube e Kick): só existe no Padrão, então ligar escolhe o Padrão e trava a lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeEscolherOTipo))]
    private bool _chatMultiplataforma;

    public bool PodeEscolherOTipo => !ChatMultiplataforma;

    /// <summary>Ao conectar no chat de uma live, mostrar também as mensagens de antes (desligado no começo).</summary>
    [ObservableProperty]
    private bool _mostrarHistoricoDoYouTube;

    partial void OnChatMultiplataformaChanged(bool value)
    {
        if (value)
            TipoDeChat = (int)Tipos.Padrao;
    }

    // Padrão
    [ObservableProperty]
    private bool _apagarMensagensAntigas;

    /// <summary>Aceita qualquer texto; só um número inteiro maior que 0 apaga as mensagens.</summary>
    [ObservableProperty]
    private string _segundosParaApagar = string.Empty;

    [ObservableProperty]
    private bool _esconderBots;

    [ObservableProperty]
    private bool _esconderGifs;

    [ObservableProperty]
    private bool _esconderOutrosCanais;

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
        ApagarMensagensAntigas = opcoes.ApagarMensagensAntigas;
        SegundosParaApagar = opcoes.SegundosParaApagar;
        EsconderBots = opcoes.EsconderBots;
        EsconderGifs = opcoes.EsconderGifs;
        EsconderOutrosCanais = opcoes.EsconderOutrosCanais;
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
        switch (TipoNaTela)
        {
            case Tipos.Padrao:
                opcoes.EnderecoPersonalizado = string.Empty;
                opcoes.ApagarMensagensAntigas = ApagarMensagensAntigas;
                opcoes.SegundosParaApagar = SegundosParaApagar;
                opcoes.EsconderBots = EsconderBots;
                opcoes.EsconderGifs = EsconderGifs;
                opcoes.EsconderOutrosCanais = EsconderOutrosCanais;
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
        estado["Chat.ApagarMensagensAntigas"] = ApagarMensagensAntigas.ToString();
        estado["Chat.SegundosParaApagar"] = SegundosParaApagar;
        estado["Chat.EsconderBots"] = EsconderBots.ToString();
        estado["Chat.EsconderGifs"] = EsconderGifs.ToString();
        estado["Chat.EsconderOutrosCanais"] = EsconderOutrosCanais.ToString();
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
