using System.Globalization;

namespace OlhoNoChat.Chat;

/// <summary>
/// Quantos estão assistindo cada canal agora ("Mostrar viewers", aba Chat). null = sem número: canal fora do ar, sem
/// canal nessa plataforma ou a plataforma não respondeu.
/// </summary>
public sealed record Viewers(long? Twitch, long? YouTube, long? Kick, long? TikTok)
{
    public static readonly Viewers Nenhum = new(null, null, null, null);

    /// <summary>A soma, só com mais de uma plataforma com número (com uma só, a soma é ela mesma).</summary>
    public long? Total
    {
        get
        {
            long?[] numeros = [Twitch, YouTube, Kick, TikTok];
            return numeros.Count(n => n != null) > 1 ? numeros.Sum(n => n ?? 0) : null;
        }
    }

    /// <summary>Tem algum número.</summary>
    public bool Algum => this != Nenhum;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// O número curto, como as plataformas mostram: "999", "1,2 mil", "12 mil", "1,5 mi". Cortado para baixo (nunca
    /// mostra mais do que tem): 1.999 = "1,9 mil".
    /// </summary>
    public static string Curto(long numero)
    {
        if (numero < 1000)
            return Math.Max(0, numero).ToString(PtBr);
        if (numero < 10_000)
            return Decimal(numero / 100, "mil");
        if (numero < 1_000_000)
            return (numero / 1000).ToString(PtBr) + " mil";
        if (numero < 10_000_000)
            return Decimal(numero / 100_000, "mi");
        return (numero / 1_000_000).ToString(PtBr) + " mi";
    }

    // Em décimos: 12 = "1,2 <unidade>"; 10 = "1 <unidade>"
    private static string Decimal(long decimos, string unidade) =>
        (decimos % 10 == 0 ? (decimos / 10).ToString(PtBr) : (decimos / 10.0).ToString("0.0", PtBr)) + " " + unidade;

    /// <summary>O número inteiro, com o ponto dos milhares: "3.135".</summary>
    public static string Inteiro(long numero) => numero.ToString("N0", PtBr);
}
