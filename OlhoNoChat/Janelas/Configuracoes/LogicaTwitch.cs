#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Twitch: a conta (conectar e desconectar acontecem na hora, sem "Salvar"), a caixa de escrever e os resgates de
/// pontos. As dicas citam o atalho de "Escrever no chat" salvo (não o que está sendo editado na aba Geral).
/// </summary>
public sealed partial class LogicaTwitch : LogicaDaPagina
{
    public const string TituloDosErros = "Conectar com a Twitch";
    public const string TextoNaoConfirmou = "A Twitch não confirmou o acesso. Confira sua internet e tente de novo.";

    private readonly AutorizacaoNoNavegador _autorizacao;
    private readonly ContaDaTwitch _conta;
    private readonly ResgatesDePontos _resgates;
    // Os avisos da conta e dos resgates chegam de outras threads: a tela é atualizada na da janela
    private readonly SynchronizationContext? _tela = SynchronizationContext.Current;
    private Atalho? _atalhoEscreverSalvo;
    private bool _conferindo;
    private bool _ligada;

    public LogicaTwitch(AutorizacaoNoNavegador autorizacao, ContaDaTwitch conta, ResgatesDePontos resgates)
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
    }

    /// <summary>Depois de "Salvar": as dicas passam a citar o atalho que acabou de ser salvo.</summary>
    public void Salvou(Opcoes opcoes)
    {
        _atalhoEscreverSalvo = opcoes.AtalhoEscrever;
        OnPropertyChanged(nameof(DicaDaCaixa));
        OnPropertyChanged(nameof(DicaDeFecharDepoisDeEnviar));
    }

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Twitch.CaixaDeDigitar"] = CaixaDeDigitar.ToString();
        estado["Twitch.FecharCaixaDepoisDeEnviar"] = FecharCaixaDepoisDeEnviar.ToString();
        estado["Twitch.MostrarResgates"] = MostrarResgates.ToString();
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

    /// <summary>"Conectar", ou "Conectar de novo" para pedir a permissão nova (emotes).</summary>
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

    private void ProblemaMudou() => NaTela(() => OnPropertyChanged(nameof(ProblemaDosResgates)));

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
        // Conectada antes da 1.0.18: a lista de emotes precisa de uma permissão nova, dada ao conectar de novo
        bool semPermissaoDeEmotes = conectada && _conta.PermissoesConferidas && !_conta.PodeLerEmotes;

        MostrarConectar = (!conectada || semPermissaoDeEmotes) && !esperando;
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
                : "Você pode escrever no chat, usar os seus emotes e mostrar os resgates de pontos.";
        }
        else
        {
            TituloDaConta = "Não conectado";
            DicaDaConta = "Para só ler o chat, não precisa conectar. Conecte para escrever no chat e mostrar os resgates de pontos.";
        }

        Foto = conectada && _conta.Foto.Length > 0 ? _conta.Foto : null;
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
