using System.Text.Json;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Quantos assistem um canal da Twitch, sem conta: a consulta pública (GQL) que o próprio site da Twitch faz para quem
/// não está logado. O número sai do <c>stream.viewersCount</c>; sem <c>stream</c>, o canal está fora do ar.
/// </summary>
public static class ViewersDaTwitch
{
    public const string Endereco = "https://gql.twitch.tv/gql";

    /// <summary>O id do site da Twitch, o mesmo que a página manda para quem assiste sem conta.</summary>
    public const string IdDoSite = "kimne78kx3ncx6brgo4mv6wki5h1ko";

    public static string Pedido(string canal) => JsonSerializer.Serialize(new
    {
        query = "query($login:String!){user(login:$login){stream{viewersCount}}}",
        variables = new { login = canal },
    });

    /// <summary>O número, ou null (fora do ar ou o canal não existe).</summary>
    /// <exception cref="JsonException">A resposta não é a esperada (a Twitch mudou).</exception>
    public static long? Ler(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        JsonElement raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("data", out JsonElement dados) || dados.ValueKind != JsonValueKind.Object
            || !dados.TryGetProperty("user", out JsonElement usuario))
            throw new JsonException("Resposta inesperada da Twitch (sem data.user).");
        if (usuario.ValueKind != JsonValueKind.Object || !usuario.TryGetProperty("stream", out JsonElement live)
            || live.ValueKind != JsonValueKind.Object)
            return null;
        return live.TryGetProperty("viewersCount", out JsonElement n) && n.TryGetInt64(out long numero) ? numero : null;
    }
}
