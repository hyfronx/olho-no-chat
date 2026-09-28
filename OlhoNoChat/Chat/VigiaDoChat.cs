#nullable enable
namespace OlhoNoChat.Chat;

/// <summary>
/// A vigia do chat Padrão: a cada 30 s pergunta à página como está a conexão com a Twitch e decide se o chat precisa
/// ser aberto de novo. A própria página reconecta quando a conexão cai (e troca uma conexão calada há 75 s); a vigia só
/// age quando o script dela não roda ou quando a Twitch fica muito tempo sem mandar nada. Nunca recarrega em sequência:
/// no mínimo 60 s entre recargas, e 5 min depois de 3 seguidas sem a conexão voltar a funcionar.
/// </summary>
public sealed class VigiaDoChat
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    // Um carregamento mais longo que isso não impede mais a pergunta
    private static readonly TimeSpan CarregamentoMaximo = TimeSpan.FromSeconds(60);
    // O script da página tem esse tempo para começar depois de a página carregar
    private static readonly TimeSpan TempoParaComecar = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SilencioMaximo = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan EntreRecargas = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EntreRecargasDepoisDeVarias = TimeSpan.FromMinutes(5);
    private const int RecargasSeguidasRapidas = 3;

    private readonly Func<DateTime> _agora;
    private DateTime _comecouACarregarEm = DateTime.MinValue;
    private DateTime _carregouEm = DateTime.MinValue;
    private DateTime _ultimaRecarga = DateTime.MinValue;
    private int _recargasSeguidas;

    public VigiaDoChat(Func<DateTime>? agora = null)
    {
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    /// <summary>Uma navegação do chat começou e ainda não terminou.</summary>
    public bool Carregando { get; private set; }

    /// <summary>Alguma página já terminou de carregar e nenhuma outra está carregando (os avisos esperam isso).</summary>
    public bool PaginaPronta => _carregouEm != DateTime.MinValue && !Carregando;

    public void ComecouACarregar()
    {
        Carregando = true;
        _comecouACarregarEm = _agora();
    }

    public void TerminouDeCarregar()
    {
        Carregando = false;
        _carregouEm = _agora();
    }

    /// <summary>A navegação nem começou (erro ao abrir o endereço).</summary>
    public void NaoCarregou() => Carregando = false;

    /// <summary>Se é hora de perguntar à página: não durante um carregamento, a não ser que ele passe de 60 s.</summary>
    public bool DevePerguntar() => !Carregando || _agora() - _comecouACarregarEm >= CarregamentoMaximo;

    /// <summary>O que fazer com a resposta da página: o motivo para abrir o chat de novo, ou null para não fazer nada.</summary>
    public string? Avaliar(SaudeDaPagina saude)
    {
        switch (saude.Estado)
        {
            case SaudeDaPagina.Situacao.Aberta:
                if (saude.SemDados > SilencioMaximo)
                    return Recarregar($"a Twitch não manda nada há {saude.SemDados.TotalSeconds:0} s");
                _recargasSeguidas = 0; // a conexão está funcionando
                return null;

            case SaudeDaPagina.Situacao.Reconectando:
                return null; // a página cuida disso

            default:
                // Sem o script (ou com erro): recarrega, dando tempo de o script começar
                if (_agora() - _carregouEm <= TempoParaComecar)
                    return null;
                return Recarregar(saude.Estado == SaudeDaPagina.Situacao.ComErro
                    ? $"o script da página deu erro ({saude.Erro})"
                    : "o script da página não está rodando");
        }
    }

    private string? Recarregar(string motivo)
    {
        DateTime agora = _agora();
        TimeSpan intervalo = _recargasSeguidas >= RecargasSeguidasRapidas ? EntreRecargasDepoisDeVarias : EntreRecargas;
        if (agora - _ultimaRecarga < intervalo)
            return null;

        _ultimaRecarga = agora;
        _recargasSeguidas++;
        return $"{motivo} (tentativa {_recargasSeguidas})";
    }
}
