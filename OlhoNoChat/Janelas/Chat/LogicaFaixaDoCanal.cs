#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

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
/// procurado na Twitch; o mesmo canal não recarrega o chat (as mensagens da tela iriam embora).
/// </summary>
public sealed partial class LogicaFaixaDoCanal : ObservableObject
{
    public const string DicaPadrao = "Ex.: seucanal, @seucanal ou o link do canal";

    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _contaConectada;
    private readonly Func<string, Task<bool?>> _canalExiste;
    private readonly Action<string> _trocarCanal;
    private bool _editorPedido;

    /// <param name="opcoes">As opções em uso (o canal salvo).</param>
    /// <param name="contaConectada">Com a conta conectada o canal é procurado na Twitch antes de trocar.</param>
    /// <param name="canalExiste">Pergunta à Twitch; null = não deu para saber (segue em frente).</param>
    /// <param name="trocarCanal">Grava o canal ("" = sair do canal) e abre o chat dele.</param>
    public LogicaFaixaDoCanal(Func<Opcoes> opcoes, Func<bool> contaConectada, Func<string, Task<bool?>> canalExiste,
        Action<string> trocarCanal)
    {
        _opcoes = opcoes;
        _contaConectada = contaConectada;
        _canalExiste = canalExiste;
        _trocarCanal = trocarCanal;
        _texto = CanalSalvo;
    }

    /// <summary>A caixa do nome precisa do foco, com o texto todo selecionado (ao abrir) ou não (depois de um erro).</summary>
    public event Action<bool>? PedirFoco;

    public string CanalSalvo => PaginaDoChat.CanalSalvo(_opcoes());

    public bool TemCanal => CanalSalvo.Length > 0;

    /// <summary>A caixa do nome está aberta: sempre sem canal, ou quando a pessoa clicou na faixa.</summary>
    public bool EditorAberto => !TemCanal || _editorPedido;

    public string TextoDoBotao => TemCanal ? "Trocar" : "Entrar no chat";

    [ObservableProperty]
    private string _texto;

    [ObservableProperty]
    private string _dica = DicaPadrao;

    [ObservableProperty]
    private bool _dicaEhErro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeConfirmar))]
    private bool _procurando;

    public bool PodeConfirmar => !Procurando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DicaDaFaixa))]
    private EstadoDaConexao _conexao;

    /// <summary>A dica da faixa fechada: a conexão e o que o clique faz.</summary>
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
            return estado == null ? "Clique para trocar de canal." : estado + "\nClique para trocar de canal.";
        }
    }

    partial void OnTextoChanged(string value)
    {
        // Enquanto o canal é procurado, o "Procurando o canal…" fica
        if (!Procurando)
            MostrarDica(null);
    }

    /// <summary>O canal salvo pode ter mudado, ou a faixa sumiu (bordas ocultas, outro tipo de chat): o editor fecha.</summary>
    public void Atualizar(bool faixaNaTela)
    {
        if (!faixaNaTela || !TemCanal)
            _editorPedido = false;
        if (!EditorAberto)
            Texto = CanalSalvo;
        MostrarDica(null);
        AvisarTudo();
    }

    public void AbrirEditor()
    {
        _editorPedido = true;
        Texto = CanalSalvo;
        MostrarDica(null);
        AvisarTudo();
        PedirFoco?.Invoke(true);
    }

    public void FecharEditor()
    {
        _editorPedido = false;
        Texto = CanalSalvo;
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
        if (string.Equals(nome, CanalSalvo, StringComparison.OrdinalIgnoreCase))
        {
            FecharEditor();
            return;
        }

        // Sem conta a Twitch não é perguntada (o chat de um canal que não existe só fica vazio)
        if (_contaConectada())
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

        Trocar(nome);
    }

    /// <summary>"Sair do canal": sem canal, o chat mostra as boas-vindas e a faixa fica aberta.</summary>
    public void SairDoCanal() => Trocar(string.Empty);

    private void Trocar(string canal)
    {
        _editorPedido = false;
        _trocarCanal(canal);
        Texto = CanalSalvo;
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
        Dica = texto ?? DicaPadrao;
        DicaEhErro = texto != null && erro;
    }

    private void AvisarTudo()
    {
        OnPropertyChanged(nameof(TemCanal));
        OnPropertyChanged(nameof(EditorAberto));
        OnPropertyChanged(nameof(TextoDoBotao));
        OnPropertyChanged(nameof(CanalSalvo));
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
