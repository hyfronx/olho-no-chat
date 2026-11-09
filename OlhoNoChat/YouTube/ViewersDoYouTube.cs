using System.Text.Json;

namespace OlhoNoChat.YouTube;

/// <summary>
/// Quantos assistem uma live do YouTube agora: o <c>updated_metadata</c>, que a página da live pede para atualizar o
/// "assistindo agora". O número exato vem em <c>updateViewershipAction.viewCount.videoViewCountRenderer.originalViewCount</c>.
/// </summary>
public static class ViewersDoYouTube
{
    public const string Caminho = "/youtubei/v1/updated_metadata?prettyPrint=false&key=";

    /// <summary>O corpo do pedido, com a versão do site que a página da live deu.</summary>
    public static string Pedido(PaginaDaLive live) => JsonSerializer.Serialize(new
    {
        context = new { client = new { clientName = "WEB", clientVersion = live.VersaoDoCliente } },
        videoId = live.IdDaLive,
    });

    /// <summary>O número, ou null (a live acabou ou a resposta veio sem ele).</summary>
    /// <exception cref="JsonException">Não é JSON.</exception>
    public static long? Ler(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        if (!documento.RootElement.TryGetProperty("actions", out JsonElement acoes) || acoes.ValueKind != JsonValueKind.Array)
            return null;
        foreach (JsonElement acao in acoes.EnumerateArray())
        {
            if (acao.ValueKind != JsonValueKind.Object || !acao.TryGetProperty("updateViewershipAction", out JsonElement audiencia)
                || !audiencia.TryGetProperty("viewCount", out JsonElement contagem)
                || !contagem.TryGetProperty("videoViewCountRenderer", out JsonElement r) || r.ValueKind != JsonValueKind.Object)
                continue;
            if (r.TryGetProperty("isLive", out JsonElement aoVivo) && aoVivo.ValueKind == JsonValueKind.False)
                return null;
            if (r.TryGetProperty("originalViewCount", out JsonElement original) && original.ValueKind == JsonValueKind.String
                && long.TryParse(original.GetString(), out long numero))
                return numero;
        }
        return null;
    }
}
