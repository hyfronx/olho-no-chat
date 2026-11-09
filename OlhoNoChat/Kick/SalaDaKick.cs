using System.Text.Json;

namespace OlhoNoChat.Kick;

/// <summary>A insígnia de assinante do canal a partir de tantos meses (a imagem que o canal escolheu).</summary>
public sealed record InsigniaDeAssinante(int Meses, string Imagem);

/// <summary>
/// O canal na API do site da Kick (<c>kick.com/api/v2/channels/{nome}</c>, sem login): o id do canal (para pedir o
/// endereço do tempo real), o id da sala do chat (o canal do Centrifugo) e as insígnias de assinante do canal.
/// </summary>
public sealed record SalaDaKick(long IdDoCanal, long IdDaSala, IReadOnlyList<InsigniaDeAssinante> Insignias)
{
    public static string Endereco(string canal) => "https://kick.com/api/v2/channels/" + Uri.EscapeDataString(canal);

    /// <exception cref="JsonException">A resposta não é a esperada (a Kick mudou).</exception>
    public static SalaDaKick Ler(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        JsonElement raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("id", out JsonElement id) || !id.TryGetInt64(out long idDoCanal)
            || !raiz.TryGetProperty("chatroom", out JsonElement sala) || sala.ValueKind != JsonValueKind.Object
            || !sala.TryGetProperty("id", out JsonElement idDaSala) || !idDaSala.TryGetInt64(out long numeroDaSala))
            throw new JsonException("Resposta inesperada da Kick (o canal sem id ou sem a sala do chat).");

        var insignias = new List<InsigniaDeAssinante>();
        if (raiz.TryGetProperty("subscriber_badges", out JsonElement lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement insignia in lista.EnumerateArray())
            {
                if (insignia.ValueKind == JsonValueKind.Object
                    && insignia.TryGetProperty("badge_image", out JsonElement imagem) && imagem.ValueKind == JsonValueKind.Object
                    && imagem.TryGetProperty("src", out JsonElement src) && src.ValueKind == JsonValueKind.String
                    && src.GetString() is { Length: > 0 } endereco)
                {
                    int meses = insignia.TryGetProperty("months", out JsonElement m) && m.TryGetInt32(out int n) ? n : 0;
                    insignias.Add(new InsigniaDeAssinante(meses, endereco));
                }
            }
        }
        return new SalaDaKick(idDoCanal, numeroDaSala, [.. insignias.OrderBy(i => i.Meses)]);
    }

    /// <summary>Quantos assistem agora (<c>livestream.viewer_count</c>), ou null com o canal fora do ar.</summary>
    /// <exception cref="JsonException">Não é JSON.</exception>
    public static long? LerViewers(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        return documento.RootElement.ValueKind == JsonValueKind.Object
               && documento.RootElement.TryGetProperty("livestream", out JsonElement live) && live.ValueKind == JsonValueKind.Object
               && live.TryGetProperty("viewer_count", out JsonElement n) && n.TryGetInt64(out long numero)
            ? numero
            : null;
    }

    /// <summary>A insígnia de quem assina há tantos meses: a dos mais meses alcançados (pelo menos a primeira); null se o canal não tem.</summary>
    public string? InsigniaPara(int meses)
    {
        InsigniaDeAssinante? escolhida = null;
        foreach (InsigniaDeAssinante insignia in Insignias)
        {
            if (insignia.Meses <= meses || escolhida == null)
                escolhida = insignia;
        }
        return escolhida?.Imagem;
    }
}
