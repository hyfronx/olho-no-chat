using System.Text.RegularExpressions;

namespace OlhoNoChat.YouTube;

/// <summary>De onde vem o chat do YouTube: a live que estiver no ar num canal (pelo @ ou pelo id) ou uma live certa.</summary>
public enum TipoDeCanalDoYouTube
{
    /// <summary>"@nome": o canal pelo nome que aparece no endereço (youtube.com/@nome).</summary>
    Arroba,

    /// <summary>"UC…": o id do canal (youtube.com/channel/UC…).</summary>
    Id,

    /// <summary>Uma live certa, pelo id do vídeo (youtube.com/watch?v=…, youtu.be/…, youtube.com/live/…).</summary>
    Live,
}

/// <summary>
/// O canal do YouTube digitado na faixa do canal: "@nome", "nome", o link do canal ou o link de uma live. Gravado nas opções
/// em <see cref="Texto"/> ("@nome", "UC…" ou "youtu.be/id"), que <see cref="Ler"/> entende de novo.
/// </summary>
public sealed partial record CanalDoYouTube(TipoDeCanalDoYouTube Tipo, string Valor)
{
    public const string DicaInvalido = "Use o @ do canal (youtube.com/@nome), o link do canal ou o link da live.";

    // O @ do YouTube: 3 a 30 letras, números, _, - e .
    [GeneratedRegex(@"^[\p{L}\p{N}_.\-]{3,30}$")]
    private static partial Regex Arroba();

    [GeneratedRegex("^UC[A-Za-z0-9_-]{22}$")]
    private static partial Regex IdDoCanal();

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdDoVideo();

    /// <summary>Como fica gravado nas opções (e como aparece na faixa).</summary>
    public string Texto => Tipo switch
    {
        TipoDeCanalDoYouTube.Arroba => "@" + Valor,
        TipoDeCanalDoYouTube.Id => Valor,
        _ => "youtu.be/" + Valor,
    };

    public override string ToString() => Texto;

    /// <summary>O canal no que foi digitado (ou gravado); null se vazio ou se não dá para entender.</summary>
    public static CanalDoYouTube? Ler(string? digitado)
    {
        string texto = (digitado ?? string.Empty).Trim();
        if (texto.Length == 0)
            return null;

        int site = IndiceDoSite(texto, out int tamanhoDoSite);
        if (site < 0)
        {
            // Sem endereço: "@nome", "nome" ou um id de canal
            if (IdDoCanal().IsMatch(texto))
                return new CanalDoYouTube(TipoDeCanalDoYouTube.Id, texto);
            string nome = texto.TrimStart('@');
            return Arroba().IsMatch(nome) ? new CanalDoYouTube(TipoDeCanalDoYouTube.Arroba, nome) : null;
        }

        string resto = texto[(site + tamanhoDoSite)..];
        string caminho = resto.Split('?', '#')[0];
        string[] partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool curto = texto.Substring(site, tamanhoDoSite).Equals("youtu.be", StringComparison.OrdinalIgnoreCase);

        // youtu.be/id
        if (curto)
            return partes.Length > 0 && IdDoVideo().IsMatch(partes[0]) ? new CanalDoYouTube(TipoDeCanalDoYouTube.Live, partes[0]) : null;

        // youtube.com/watch?v=id (também com outros parâmetros antes)
        if (partes.Length > 0 && partes[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            Match v = Regex.Match(resto, @"[?&]v=([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])");
            return v.Success ? new CanalDoYouTube(TipoDeCanalDoYouTube.Live, v.Groups[1].Value) : null;
        }

        if (partes.Length < 1)
            return null;

        // youtube.com/@nome (/live, /streams, /videos...)
        if (partes[0].StartsWith('@'))
        {
            string nome = Uri.UnescapeDataString(partes[0][1..]);
            return Arroba().IsMatch(nome) ? new CanalDoYouTube(TipoDeCanalDoYouTube.Arroba, nome) : null;
        }

        if (partes.Length < 2)
            return null;

        // youtube.com/channel/UC…, youtube.com/live/id
        string segunda = partes[1];
        return partes[0].ToLowerInvariant() switch
        {
            "channel" when IdDoCanal().IsMatch(segunda) => new CanalDoYouTube(TipoDeCanalDoYouTube.Id, segunda),
            "live" or "shorts" or "embed" when IdDoVideo().IsMatch(segunda) => new CanalDoYouTube(TipoDeCanalDoYouTube.Live, segunda),
            _ => null,
        };
    }

    // youtube.com (com www., m. ou music.) ou youtu.be
    private static int IndiceDoSite(string texto, out int tamanho)
    {
        foreach (string site in new[] { "youtube.com", "youtu.be" })
        {
            int indice = texto.IndexOf(site, StringComparison.OrdinalIgnoreCase);
            if (indice >= 0)
            {
                tamanho = site.Length;
                return indice;
            }
        }
        tamanho = 0;
        return -1;
    }
}
