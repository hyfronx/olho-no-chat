#nullable enable
namespace OlhoNoChat.Som;

/// <summary>
/// "Quando tocar" da aba Som: a primeira mensagem toca; as seguintes só depois de tantos segundos desde a última vez
/// que o som tocou de verdade (pedidos ignorados não contam).
/// </summary>
public sealed class RegraQuandoTocar
{
    private DateTime? _ultimoSom;

    /// <summary>0 = em toda mensagem nova.</summary>
    public int Segundos { get; set; }

    public bool Permite(DateTime agora) =>
        Segundos <= 0 || _ultimoSom is not { } ultimo || agora - ultimo >= TimeSpan.FromSeconds(Segundos);

    public void Tocou(DateTime agora) => _ultimoSom = agora;
}
