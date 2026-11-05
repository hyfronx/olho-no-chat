using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Twitch: a conta (conectar e desconectar acontecem na hora, sem "Salvar"), a caixa de escrever, os resgates de
/// pontos e o AutoMod. As dicas citam os atalhos salvos (não os que estão sendo editados na aba Geral).
/// </summary>
public sealed partial class LogicaTwitch : LogicaDaPagina
{
    public const string TituloDosErros = "Conectar com a Twitch";
    public const string TextoNaoConfirmou = "A Twitch não confirmou o acesso. Confira sua internet e tente de novo.";

    private readonly AutorizacaoNoNavegador _autorizacao;
    private readonly ContaDaTwitch _conta;
    private readonly EventSubDaTwitch _resgates;
    // Os avisos da conta e dos resgates chegam de outras threads: a tela é atualizada na da janela
    private readonly SynchronizationContext? _tela = SynchronizationContext.Current;
    private Atalho? _atalhoEscreverSalvo;
    private Atalho? _atalhoAprovarSalvo;
    private Atalho? _atalhoRecusarSalvo;
    private bool _conferindo;
    private bool _ligada;

    public LogicaTwitch(AutorizacaoNoNavegador autorizacao, ContaDaTwitch conta, EventSubDaTwitch resgates)
    {
        _autorizacao = autorizacao;
        _conta = conta;
        _resgates = resgates;
        MostrarConta();
    }

    /// <summary>Um erro ao conectar, para mostrar numa caixa de mensagem (título <see cref="TituloDosErros"/>).</summary>
    public event Action<string>? Erro;

    /// <summary>Terminou de conectar: a janela volta para a frente.</summary>
    public event Action? TerminouDeConectar;

    // ----- Escrever no chat e resgates -----

    /// <summary>0 = "Do Olho no Chat", 1 = "Da própria Twitch".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DicaDaCaixa))]
    private int _caixaDeDigitar;

    [ObservableProperty]
    private bool _fecharCaixaDepoisDeEnviar;

    [ObservableProperty]
    private bool _mostrarResgates;

    [ObservableProperty]
    private bool _aprovarAutoMod;

    public string DicaDoAutoMod => TextoDaDicaDoAutoMod(_atalhoAprovarSalvo, _atalhoRecusarSalvo);

    /// <summary>Por que o AutoMod não funciona com a conta ("" = funciona, ou sem conta: a dica da conta já diz).</summary>
    public string ProblemaDoAutoMod => _conta.EstaConectada && _conta.PermissoesConferidas && !_conta.PodeModerarAutoMod
        ? "Falta uma permissão nova: clique em \"Conectar de novo\", acima."
        : string.Empty;

    /// <summary>Se o AutoMod funciona no canal aberto agora ("" = não pedido: opção desligada, sem conta ou sem chat Padrão).</summary>
    public string SituacaoDoAutoMod => TextoDaSituacao(_resgates.AutoMod);

    public static string TextoDaSituacao(EventSubDaTwitch.SituacaoDoAutoMod situacao) => situacao switch
    {
        EventSubDaTwitch.SituacaoDoAutoMod.Funcionando => "Funcionando no canal aberto agora.",
        EventSubDaTwitch.SituacaoDoAutoMod.NaoModera => "A sua conta não é dona nem moderadora do canal aberto: nada aparece nele.",
        EventSubDaTwitch.SituacaoDoAutoMod.Falhou => "Não deu para falar com a Twitch agora. O AutoMod volta quando o chat carregar de novo ou você clicar em Salvar.",
        _ => string.Empty,
    };

    /// <summary>A dica do AutoMod. A frase dos atalhos só cita os que existem.</summary>
    public static string TextoDaDicaDoAutoMod(Atalho? aprovar, Atalho? recusar)
    {
        const string comum = "Quando o AutoMod segura uma mensagem num canal em que a sua conta é dona ou moderadora, ela " +
                             "aparece embaixo do chat Padrão, uma por vez (a mais antiga primeiro). Com as bordas, use os botões";
        string atalhos = (Atalho.Existe(aprovar), Atalho.Existe(recusar)) switch
        {
            (true, true) => $"; no jogo, {aprovar} aprova e {recusar} recusa.",
            (true, false) => $"; no jogo, {aprovar} aprova.",
            (false, true) => $"; no jogo, {recusar} recusa.",
            _ => ".",
        };
        return comum + atalhos;
    }

    public string DicaDaCaixa => TextoDaDicaDaCaixa(CaixaDeDigitar == 1, _atalhoEscreverSalvo);

    public string DicaDeFecharDepoisDeEnviar => TextoDaDicaDeFechar(_atalhoEscreverSalvo);

    /// <summary>Por que a Twitch recusou os resgates ("" = sem problema).</summary>
    public string ProblemaDosResgates => _resgates.Problema;

    /// <summary>A dica da "Caixa de digitar". A frase do atalho só aparece quando existe o atalho.</summary>
    public static string TextoDaDicaDaCaixa(bool caixaDaTwitch, Atalho? atalhoEscrever)
    {
        string noJogo = Atalho.Existe(atalhoEscrever)
            ? $" No jogo, aperte {atalhoEscrever} para abrir ou fechar a caixa por cima do jogo."
            : string.Empty;
        return caixaDaTwitch
            ? "A caixa da Twitch, com a lista de emotes, respostas e comandos (só no Chat oficial da Twitch; no Padrão fica a do " +
              "Olho no Chat). Abra com o balão de conversa, na barra laranja. Para enviar, entre na sua conta da Twitch dentro da " +
              "janela do chat (uma vez): abra a caixa, clique em \"Chat\", embaixo, e depois em \"Faça login\"." + noJogo
            : "A caixa \"Escrever no chat…\" embaixo do chat: abra com o balão de conversa, na barra laranja. Envia com a conta " +
              "conectada acima." + noJogo;
    }

    /// <summary>A dica de "Fechar a caixa depois de enviar".</summary>
    public static string TextoDaDicaDeFechar(Atalho? atalhoEscrever)
    {
        const string comum = "Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. " +
                             "Desligado, ela continua aberta para a próxima mensagem";
        return Atalho.Existe(atalhoEscrever)
            ? $"{comum}; feche com {atalhoEscrever} de novo, Esc ou o × da caixa."
            : comum + ".";
    }

    public override void Carregar(Opcoes opcoes)
    {
        _atalhoEscreverSalvo = opcoes.AtalhoEscrever;
        CaixaDeDigitar = opcoes.CaixaDaTwitch ? 1 : 0;
        FecharCaixaDepoisDeEnviar = opcoes.FecharCaixaDepoisDeEnviar;
        MostrarResgates = opcoes.MostrarResgates;
        AprovarAutoMod = opcoes.AprovarAutoMod;
        _atalhoAprovarSalvo = opcoes.AtalhoAprovarAutoMod;
        _atalhoRecusarSalvo = opcoes.AtalhoRecusarAutoMod;
        OnPropertyChanged(nameof(DicaDoAutoMod));
        OnPropertyChanged(nameof(DicaDaCaixa));
        OnPropertyChanged(nameof(DicaDeFecharDepoisDeEnviar));

        MostrarConta();
        // A foto e uma conferência nova do acesso
        if (_conta.EstaConectada)
            _ = _conta.VerificarAsync();
    }

    public override void Gravar(Opcoes opcoes)
    {
        opcoes.CaixaDaTwitch = CaixaDeDigitar == 1;
        opcoes.FecharCaixaDepoisDeEnviar = FecharCaixaDepoisDeEnviar;
        opcoes.MostrarResgates = MostrarResgates;
        opcoes.AprovarAutoMod = AprovarAutoMod;
    }

    /// <summary>Depois de "Salvar": as dicas passam a citar o atalho que acabou de ser salvo.</summary>
    public void Salvou(Opcoes opcoes)
    {
        _atalhoEscreverSalvo = opcoes.AtalhoEscrever;
        _atalhoAprovarSalvo = opcoes.AtalhoAprovarAutoMod;
        _atalhoRecusarSalvo = opcoes.AtalhoRecusarAutoMod;
        OnPropertyChanged(nameof(DicaDaCaixa));
        OnPropertyChanged(nameof(DicaDeFecharDepoisDeEnviar));
        OnPropertyChanged(nameof(DicaDoAutoMod));
    }

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Twitch.CaixaDeDigitar"] = CaixaDeDigitar.ToString();
        estado["Twitch.FecharCaixaDepoisDeEnviar"] = FecharCaixaDepoisDeEnviar.ToString();
        estado["Twitch.MostrarResgates"] = MostrarResgates.ToString();
        estado["Twitch.AprovarAutoMod"] = AprovarAutoMod.ToString();
    }

    // ----- Conta -----

    [ObservableProperty]
    private string _tituloDaConta = string.Empty;

    [ObservableProperty]
    private string _dicaDaConta = string.Empty;

    /// <summary>O endereço da foto do perfil (null sem conta ou sem foto).</summary>
    [ObservableProperty]
    private string? _foto;

    [ObservableProperty]
    private bool _mostrarConectar;

    /// <summary>"Conectar", ou "Conectar de novo" para pedir uma permissão nova (emotes, AutoMod).</summary>
    [ObservableProperty]
    private string _textoConectar = "Conectar";

    [ObservableProperty]
    private bool _mostrarCancelar;

    [ObservableProperty]
    private bool _mostrarDesconectar;

    /// <summary>A página está na tela: acompanha a conta e os resgates.</summary>
    public void Ligar()
    {
        if (_ligada)
            return;
        _ligada = true;
        _conta.Mudou += ContaMudou;
        _resgates.ProblemaMudou += ProblemaMudou;
        MostrarConta();
        OnPropertyChanged(nameof(ProblemaDosResgates));
        OnPropertyChanged(nameof(SituacaoDoAutoMod));
    }

    public void Desligar()
    {
        if (!_ligada)
            return;
        _ligada = false;
        _conta.Mudou -= ContaMudou;
        _resgates.ProblemaMudou -= ProblemaMudou;
    }

    private void ContaMudou() => NaTela(MostrarConta);

    private void ProblemaMudou() => NaTela(() =>
    {
        OnPropertyChanged(nameof(ProblemaDosResgates));
        OnPropertyChanged(nameof(SituacaoDoAutoMod));
    });

    private void NaTela(Action acao)
    {
        if (_tela == null || _tela == SynchronizationContext.Current)
            acao();
        else
            _tela.Post(_ => acao(), null);
    }

    private void MostrarConta()
    {
        bool esperando = _autorizacao.EstaEsperando;
        bool conectada = _conta.EstaConectada;
        // Conectada antes da 1.0.18 (emotes) ou da 2.7.0 (AutoMod): falta uma permissão, dada ao conectar de novo
        bool semPermissaoDeEmotes = conectada && _conta.PermissoesConferidas && !_conta.PodeLerEmotes;
        bool semPermissaoDoAutoMod = conectada && _conta.PermissoesConferidas && !_conta.PodeModerarAutoMod;

        MostrarConectar = (!conectada || semPermissaoDeEmotes || semPermissaoDoAutoMod) && !esperando;
        TextoConectar = conectada ? "Conectar de novo" : "Conectar";
        MostrarCancelar = esperando;
        MostrarDesconectar = conectada && !esperando;

        if (esperando)
        {
            TituloDaConta = _conferindo ? "Conectando…" : "Esperando você autorizar…";
            DicaDaConta = "Termine no navegador que abriu: clique em \"Autorizar\" na página da Twitch.";
        }
        else if (conectada)
        {
            TituloDaConta = "Conectado como " + _conta.NomeMostrado;
            DicaDaConta = semPermissaoDeEmotes
                ? "Para ver os seus emotes na caixa de escrever, clique em \"Conectar de novo\" (a Twitch pede uma permissão nova)."
                : semPermissaoDoAutoMod
                    ? "Para aprovar as mensagens barradas pelo AutoMod, clique em \"Conectar de novo\" (a Twitch pede uma permissão nova)."
                    : "Você pode escrever no chat, usar os seus emotes, mostrar os resgates de pontos e aprovar as mensagens barradas pelo AutoMod.";
        }
        else
        {
            TituloDaConta = "Não conectado";
            DicaDaConta = "Para só ler o chat, não precisa conectar. Conecte para escrever no chat e mostrar os resgates de pontos.";
        }

        Foto = conectada && _conta.Foto.Length > 0 ? _conta.Foto : null;
        OnPropertyChanged(nameof(ProblemaDoAutoMod));
    }

    [RelayCommand]
    private async Task ConectarAsync()
    {
        Task<AutorizacaoNoNavegador.Resultado> espera = _autorizacao.ConectarAsync();
        MostrarConta();
        AutorizacaoNoNavegador.Resultado resultado = await espera;

        switch (resultado.Fim)
        {
            case AutorizacaoNoNavegador.Fim.JaEmAndamento:
                return;
            case AutorizacaoNoNavegador.Fim.PortasOcupadas:
                MostrarConta();
                Erro?.Invoke(AutorizacaoNoNavegador.TextoPortasOcupadas);
                return;
            case AutorizacaoNoNavegador.Fim.Token:
                _conferindo = true;
                TituloDaConta = "Conectando…";
                try
                {
                    if (!await _conta.ConectarAsync(resultado.Token))
                        Erro?.Invoke(TextoNaoConfirmou);
                }
                finally
                {
                    _conferindo = false;
                }
                break;
        }

        MostrarConta();
        TerminouDeConectar?.Invoke();
    }

    [RelayCommand]
    private void CancelarConexao() => _autorizacao.Cancelar();

    /// <summary>Sem confirmação: a conta é esquecida na hora e o acesso é cancelado na Twitch em segundo plano.</summary>
    [RelayCommand]
    private void Desconectar()
    {
        _conta.Desconectar();
        MostrarConta();
    }
}
