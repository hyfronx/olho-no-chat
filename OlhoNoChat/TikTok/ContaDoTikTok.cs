using Microsoft.Extensions.Logging;

namespace OlhoNoChat.TikTok;

/// <summary>
/// A conta do TikTok conectada na aba TikTok (opcional): com ela, o chat do TikTok é lido como quem está logado, porque o
/// TikTok não manda para quem assiste sem conta as mensagens de algumas pessoas. Só a sessão fica guardada, no
/// <see cref="CofreDoTikTok"/>; conectar e desconectar valem na hora. Pode ser usada de qualquer thread.
/// </summary>
public sealed class ContaDoTikTok
{
    private readonly CofreDoTikTok _cofre;
    private readonly ILogger _log;
    private readonly object _trava = new();
    private SessaoDoTikTok? _conta;

    public ContaDoTikTok(ILogger<ContaDoTikTok> log)
        : this(log, new CofreDoTikTok(InfoDoApp.PastaDoCofre, InfoDoApp.ChaveDoCofre))
    {
    }

    public ContaDoTikTok(ILogger log, CofreDoTikTok cofre)
    {
        _log = log;
        _cofre = cofre;
        _conta = cofre.Abrir(out ProtecaoDaSessao protecao);
        Protecao = protecao;
    }

    /// <summary>A conta mudou: conectou, desconectou ou o TikTok recusou a sessão (em qualquer thread).</summary>
    public event Action? Mudou;

    /// <summary>A conta conectada, ou null.</summary>
    public SessaoDoTikTok? Conta
    {
        get
        {
            lock (_trava)
                return _conta;
        }
    }

    /// <summary>O cookie da sessão, para a leitura do chat, ou null sem conta.</summary>
    public string? Sessao => Conta?.Sessao;

    /// <summary>Como a sessão está protegida no computador.</summary>
    public ProtecaoDaSessao Protecao { get; private set; }

    /// <summary>A última conta saiu porque o TikTok não aceitou mais a sessão (a aba TikTok avisa até conectar de novo).</summary>
    public bool FoiRecusada { get; private set; }

    /// <summary>Guarda a conta que acabou de entrar na janela de login.</summary>
    public void Guardar(SessaoDoTikTok conta)
    {
        ProtecaoDaSessao protecao = _cofre.Guardar(conta);
        lock (_trava)
        {
            _conta = conta;
            Protecao = protecao;
            FoiRecusada = false;
        }
        _log.LogInformation("TikTok: conta @{Login} conectada (proteção: {Protecao}).", conta.Login, protecao);
        Mudou?.Invoke();
    }

    /// <summary>Desconectar (a pessoa pediu): apaga a sessão do computador.</summary>
    public void Desconectar() => Esquecer(recusada: false);

    /// <summary>O TikTok não aceita mais a sessão (saiu da conta, trocou a senha, venceu): apaga e avisa.</summary>
    public void SessaoRecusada() => Esquecer(recusada: true);

    private void Esquecer(bool recusada)
    {
        lock (_trava)
        {
            if (_conta == null)
                return;
            _log.LogInformation("TikTok: conta @{Login} desconectada ({Motivo}).", _conta.Login, recusada ? "o TikTok recusou a sessão" : "pedido");
            _conta = null;
            FoiRecusada = recusada;
        }
        _cofre.Apagar();
        Mudou?.Invoke();
    }
}
