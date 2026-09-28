#nullable enable
namespace OlhoNoChat.Chat;

/// <summary>
/// Os limites da recuperação quando um processo do navegador interno falha, para não ficar recriando nem recarregando
/// sem parar ao lado do jogo: no máximo 3 vezes em 5 minutos, contadas à parte para o navegador e para a página. Uma
/// página travada só é recarregada depois de 20 s seguidos travada (o aviso chega repetido, também quando o computador
/// só está ocupado, como um jogo carregando).
/// </summary>
public sealed class RecuperacaoDoNavegador
{
    private static readonly TimeSpan Periodo = TimeSpan.FromMinutes(5);
    private const int MaximoNoPeriodo = 3;
    private static readonly TimeSpan TravadaPorMuitoTempo = TimeSpan.FromSeconds(20);
    // Um aviso de travada depois de tanto tempo sem aviso começa outra contagem
    private static readonly TimeSpan NovaTravada = TimeSpan.FromSeconds(60);

    private readonly Func<DateTime> _agora;
    private readonly Queue<DateTime> _navegadorRecriado = new();
    private readonly Queue<DateTime> _paginaRecarregada = new();
    private DateTime? _travadaDesde;
    private DateTime _ultimoAvisoDeTravada = DateTime.MinValue;

    public RecuperacaoDoNavegador(Func<DateTime>? agora = null)
    {
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    /// <summary>Conta uma recriação do navegador; false na quarta em 5 minutos (aí o app fecha).</summary>
    public bool PodeRecriarONavegador() => Contar(_navegadorRecriado, out _);

    /// <summary>
    /// Conta uma recarga da página depois de uma falha; false na quarta em 5 minutos, e <paramref name="tentarEm"/> diz
    /// quando a próxima será permitida.
    /// </summary>
    public bool PodeRecarregarAPagina(out TimeSpan tentarEm) => Contar(_paginaRecarregada, out tentarEm);

    /// <summary>Mais um aviso de página travada: true quando ela já está travada há 20 s (e a contagem recomeça).</summary>
    public bool TravadaHaMuitoTempo()
    {
        DateTime agora = _agora();
        if (_travadaDesde == null || agora - _ultimoAvisoDeTravada > NovaTravada)
            _travadaDesde = agora;
        _ultimoAvisoDeTravada = agora;

        if (agora - _travadaDesde.Value < TravadaPorMuitoTempo)
            return false;
        _travadaDesde = null;
        return true;
    }

    /// <summary>Uma página carregou: uma travada de antes não conta mais.</summary>
    public void PaginaCarregou() => _travadaDesde = null;

    private bool Contar(Queue<DateTime> vezes, out TimeSpan tentarEm)
    {
        DateTime agora = _agora();
        while (vezes.Count > 0 && agora - vezes.Peek() > Periodo)
            vezes.Dequeue();

        if (vezes.Count >= MaximoNoPeriodo)
        {
            tentarEm = vezes.Peek() + Periodo - agora;
            return false;
        }

        vezes.Enqueue(agora);
        tentarEm = TimeSpan.Zero;
        return true;
    }
}
