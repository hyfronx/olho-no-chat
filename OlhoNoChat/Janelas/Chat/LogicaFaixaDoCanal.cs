using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>Como está a conexão do chat do canal (o ponto colorido da faixa).</summary>
public enum EstadoDaConexao
{
    /// <summary>Página sem canal (boas-vindas, endereço personalizado): sem ponto.</summary>
    Nenhum,
    Conectando,
    Conectado,
    SemConexao,
}

/// <summary>
/// A faixa do canal sem a tela: o único lugar para trocar o canal. Fechada, mostra o canal e o ponto da conexão; aberta,
/// a caixa do nome (sempre aberta enquanto não há canal). O nome é conferido antes de trocar e, com a conta conectada,
/// procurado na Twitch; o mesmo canal não recarrega o chat (as mensagens da tela iriam embora). No Chat Multiplataforma
/// a faixa tem também o canal do YouTube (e o ponto dele), trocado junto.
/// </summary>
public sealed partial class LogicaFaixaDoCanal : ObservableObject
{
    public const string DicaPadrao = "Ex.: seucanal, @seucanal ou o link do canal";
    public const string DicaComYouTube = "Twitch: o nome ou o link do canal. YouTube: o @ do canal, o link do canal ou o link da live.";

    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _contaConectada;
    private readonly Func<string, Task<bool?>> _canalExiste;
    private readonly Action<string, string> _trocarCanal;
    private bool _editorPedido;

    /// <param name="opcoes">As opções em uso (o canal salvo).</param>
    /// <param name="contaConectada">Com a conta conectada o canal é procurado na Twitch antes de trocar.</param>
    /// <param name="canalExiste">Pergunta à Twitch; null = não deu para saber (segue em frente).</param>
    /// <param name="trocarCanal">
    /// Grava o canal da Twitch e o do YouTube ("" = sem canal; o do YouTube como em <see cref="CanalDoYouTube.Texto"/>) e
    /// abre o chat deles.
    /// </param>
    public LogicaFaixaDoCanal(Func<Opcoes> opcoes, Func<bool> contaConectada, Func<string, Task<bool?>> canalExiste,
        Action<string, string> trocarCanal)
    {
        _opcoes = opcoes;
        _contaConectada = contaConectada;
        _canalExiste = canalExiste;
        _trocarCanal = trocarCanal;
        _texto = CanalSalvo;
        _textoDoYouTube = YouTubeSalvo;
        _dica = DicaInicial;
    }

    /// <summary>A caixa do nome precisa do foco, com o texto todo selecionado (ao abrir) ou não (depois de um erro).</summary>
    public event Action<bool>? PedirFoco;

    /// <summary>A caixa do YouTube precisa do foco (canal do YouTube errado, ou Chat Multiplataforma ligado sem ele).</summary>
    public event Action? PedirFocoNoYouTube;

    public string CanalSalvo => PaginaDoChat.CanalSalvo(_opcoes());

    public bool TemCanal => CanalSalvo.Length > 0;

    /// <summary>Chat Multiplataforma: a faixa mostra também o canal do YouTube.</summary>
    public bool ComYouTube => _opcoes().ChatMultiplataforma;

    /// <summary>O canal do YouTube salvo ("@nome", "UC…" ou "youtu.be/id"), ou "".</summary>
    public string YouTubeSalvo => CanalDoYouTube.Ler(_opcoes().CanalDoYouTube)?.Texto ?? string.Empty;

    /// <summary>O canal do YouTube aparece na faixa fechada.</summary>
    public bool YouTubeNaFaixa => ComYouTube && YouTubeSalvo.Length > 0;

    private string DicaInicial => ComYouTube ? DicaComYouTube : DicaPadrao;

    /// <summary>A caixa do nome está aberta: sempre sem canal, ou quando a pessoa clicou na faixa.</summary>
    public bool EditorAberto => !TemCanal || _editorPedido;

    public string TextoDoBotao => TemCanal ? "Trocar" : "Entrar no chat";

    [ObservableProperty]
    private string _texto;

    /// <summary>O canal do YouTube na caixa (Chat Multiplataforma).</summary>
    [ObservableProperty]
    private string _textoDoYouTube;

    [ObservableProperty]
    private string _dica;

    [ObservableProperty]
    private bool _dicaEhErro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeConfirmar))]
    private bool _procurando;

    public bool PodeConfirmar => !Procurando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DicaDaFaixa))]
    private EstadoDaConexao _conexao;

    /// <summary>A leitura do chat do YouTube (o ponto do YouTube na faixa).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DicaDaFaixa))]
    private EstadoDoYouTube _conexaoDoYouTube;

    /// <summary>A dica da faixa fechada: a conexão (das duas plataformas no Chat Multiplataforma) e o que o clique faz.</summary>
    public string DicaDaFaixa
    {
        get
        {
            string canal = CanalSalvo;
            string? estado = Conexao switch
            {
                EstadoDaConexao.Conectando => $"Conectando ao chat de {canal}…",
                EstadoDaConexao.Conectado => $"Conectado ao chat de {canal}.",
                EstadoDaConexao.SemConexao => "Sem conexão com o chat. Tentando de novo…",
                _ => null,
            };
            if (YouTubeNaFaixa)
            {
                string twitch = estado == null ? string.Empty : "Twitch: " + estado + "\n";
                estado = twitch + "YouTube: " + TextoDoEstadoDoYouTube(ConexaoDoYouTube, YouTubeSalvo);
            }
            return estado == null ? "Clique para trocar de canal." : estado + "\nClique para trocar de canal.";
        }
    }

    /// <summary>O estado da leitura do YouTube em palavras.</summary>
    public static string TextoDoEstadoDoYouTube(EstadoDoYouTube estado, string canal) => estado switch
    {
        EstadoDoYouTube.Procurando => $"procurando a live de {canal}…",
        EstadoDoYouTube.Conectado => $"lendo o chat da live de {canal}.",
        EstadoDoYouTube.EsperandoALive => $"{canal} não está ao vivo. Esperando a live começar…",
        EstadoDoYouTube.CanalNaoExiste => $"não achei o canal {canal} no YouTube. Confira o nome.",
        EstadoDoYouTube.SemConexao => "sem conexão com o YouTube. Tentando de novo…",
        _ => "desligado.",
    };

    partial void OnTextoChanged(string value)
    {
        // Enquanto o canal é procurado, o "Procurando o canal…" fica
        if (!Procurando)
            MostrarDica(null);
    }

    partial void OnTextoDoYouTubeChanged(string value)
    {
        if (!Procurando)
            MostrarDica(null);
    }

    /// <summary>O canal salvo pode ter mudado, ou a faixa sumiu (bordas ocultas, outro tipo de chat): o editor fecha.</summary>
    public void Atualizar(bool faixaNaTela)
    {
        if (!faixaNaTela || !TemCanal)
            _editorPedido = false;
        if (!EditorAberto)
        {
            Texto = CanalSalvo;
            TextoDoYouTube = YouTubeSalvo;
        }
        MostrarDica(null);
        AvisarTudo();
    }

    public void AbrirEditor()
    {
        _editorPedido = true;
        Texto = CanalSalvo;
        TextoDoYouTube = YouTubeSalvo;
        MostrarDica(null);
        AvisarTudo();
        PedirFoco?.Invoke(true);
    }

    /// <summary>Chat Multiplataforma ligado pela barra sem um canal do YouTube: a faixa abre na caixa dele.</summary>
    public void AbrirEditorNoYouTube()
    {
        _editorPedido = true;
        Texto = CanalSalvo;
        TextoDoYouTube = YouTubeSalvo;
        MostrarDica(null);
        AvisarTudo();
        PedirFocoNoYouTube?.Invoke();
    }

    public void FecharEditor()
    {
        _editorPedido = false;
        Texto = CanalSalvo;
        TextoDoYouTube = YouTubeSalvo;
        MostrarDica(null);
        AvisarTudo();
    }

    /// <summary>Esc na caixa: com canal fecha o editor; sem canal só volta a dica.</summary>
    public void Esc()
    {
        if (TemCanal)
            FecharEditor();
        else
            MostrarDica(null);
    }

    /// <summary>"Entrar no chat" / "Trocar", ou Enter na caixa.</summary>
    public async Task ConfirmarAsync()
    {
        if (Procurando)
            return;

        string nome = NomesDaTwitch.Extrair(Texto);
        if (nome.Length == 0)
        {
            Erro("Digite o nome do canal.");
            return;
        }
        if (!NomesDaTwitch.EhValido(nome))
        {
            Erro(NomesDaTwitch.DicaNomeInvalido);
            return;
        }

        nome = nome.ToLowerInvariant();

        // Chat Multiplataforma: o do YouTube pode ficar vazio (só a Twitch)
        string youTube = YouTubeSalvo;
        if (ComYouTube)
        {
            if (string.IsNullOrWhiteSpace(TextoDoYouTube))
            {
                youTube = string.Empty;
            }
            else if (CanalDoYouTube.Ler(TextoDoYouTube) is { } canalDoYouTube)
            {
                youTube = canalDoYouTube.Texto;
            }
            else
            {
                MostrarDica(CanalDoYouTube.DicaInvalido, erro: true);
                PedirFocoNoYouTube?.Invoke();
                return;
            }
        }

        bool mesmaTwitch = string.Equals(nome, CanalSalvo, StringComparison.OrdinalIgnoreCase);
        if (mesmaTwitch && youTube == YouTubeSalvo)
        {
            FecharEditor();
            return;
        }

        // Sem conta a Twitch não é perguntada (o chat de um canal que não existe só fica vazio)
        if (!mesmaTwitch && _contaConectada())
        {
            Procurando = true;
            MostrarDica("Procurando o canal…", erro: false);
            bool? existe;
            try
            {
                existe = await _canalExiste(nome);
            }
            finally
            {
                Procurando = false;
            }

            if (existe == false)
            {
                Erro($"Não achei o canal \"{nome}\" na Twitch. Confira o nome.");
                return;
            }
        }

        Trocar(nome, youTube);
    }

    /// <summary>"Sair do canal": sem canal (nem o do YouTube), o chat mostra as boas-vindas e a faixa fica aberta.</summary>
    public void SairDoCanal() => Trocar(string.Empty, ComYouTube ? string.Empty : YouTubeSalvo);

    private void Trocar(string canal, string youTube)
    {
        _editorPedido = false;
        _trocarCanal(canal, youTube);
        Texto = CanalSalvo;
        TextoDoYouTube = YouTubeSalvo;
        MostrarDica(null);
        AvisarTudo();
    }

    private void Erro(string texto)
    {
        MostrarDica(texto, erro: true);
        PedirFoco?.Invoke(false);
    }

    private void MostrarDica(string? texto, bool erro = true)
    {
        Dica = texto ?? DicaInicial;
        DicaEhErro = texto != null && erro;
    }

    private void AvisarTudo()
    {
        OnPropertyChanged(nameof(TemCanal));
        OnPropertyChanged(nameof(EditorAberto));
        OnPropertyChanged(nameof(TextoDoBotao));
        OnPropertyChanged(nameof(CanalSalvo));
        OnPropertyChanged(nameof(ComYouTube));
        OnPropertyChanged(nameof(YouTubeSalvo));
        OnPropertyChanged(nameof(YouTubeNaFaixa));
        OnPropertyChanged(nameof(DicaDaFaixa));
    }

    // --- Conexão ---------------------------------------------------------------------------------------------

    /// <summary>Uma página começou a carregar: só as páginas de canal têm o ponto (conectando).</summary>
    public void PaginaComecouACarregar(bool paginaDeCanal) =>
        Conexao = paginaDeCanal ? EstadoDaConexao.Conectando : EstadoDaConexao.Nenhum;

    /// <summary>
    /// A página terminou de carregar. Uma página de canal que falha fica "sem conexão"; o chat oficial conecta sozinho,
    /// então carregar é estar conectado (o Padrão avisa pela página, ver <see cref="PaginaAvisou"/>).
    /// </summary>
    public void PaginaCarregou(bool sucesso, bool chatOficial)
    {
        if (!sucesso)
            Conexao = Conexao == EstadoDaConexao.Nenhum ? EstadoDaConexao.Nenhum : EstadoDaConexao.SemConexao;
        else if (chatOficial)
            Conexao = EstadoDaConexao.Conectado;
    }

    /// <summary>A página do Padrão disse como está a conexão com a Twitch.</summary>
    public void PaginaAvisou(EstadoDaConexao estado) => Conexao = estado;
}
