using System.Text.Json;
using System.Text.RegularExpressions;

namespace OlhoNoChat.YouTube;

/// <summary>
/// O que o leitor tira da página da live (youtube.com/@nome/live, /channel/UC…/live ou /watch?v=…) para pedir o chat à
/// API interna do YouTube (InnerTube): o id da live, a chave da API, a versão do site e a primeira "continuação" do chat.
/// </summary>
public sealed partial record PaginaDaLive(string IdDaLive, string Chave, string VersaoDoCliente, string Continuacao)
{
    // A live no ar: o /live do canal leva ao vídeo dela; sem live, ao próprio canal
    [GeneratedRegex("""<link rel="canonical" href="[^"]*/watch\?v=([A-Za-z0-9_-]{11})""")]
    private static partial Regex LinkDaLive();

    [GeneratedRegex("""INNERTUBE_API_KEY":\s*"([^"]+)""")]
    private static partial Regex ChaveDaApi();

    [GeneratedRegex("""INNERTUBE_CONTEXT_CLIENT_VERSION":\s*"([^"]+)""")]
    private static partial Regex VersaoDoSite();

    // Live que já acabou (o chat dela é a reprise)
    [GeneratedRegex("""isReplay":\s*true""")]
    private static partial Regex Reprise();

    [GeneratedRegex("""continuation":\s*"([^"]+)""")]
    private static partial Regex ContinuacaoDoChat();

    /// <summary>O endereço da página que leva à live do canal (ou à live certa).</summary>
    public static string Caminho(CanalDoYouTube canal) => canal.Tipo switch
    {
        TipoDeCanalDoYouTube.Arroba => "/@" + Uri.EscapeDataString(canal.Valor) + "/live",
        TipoDeCanalDoYouTube.Id => "/channel/" + canal.Valor + "/live",
        _ => "/watch?v=" + canal.Valor,
    };

    /// <summary>
    /// A live da página; null quando o canal não está ao vivo (a página é a do canal, um vídeo comum ou uma live que já
    /// acabou). Uma live sem a chave ou a versão é uma página que o leitor não entende mais: dá erro.
    /// </summary>
    public static PaginaDaLive? Ler(string html)
    {
        Match live = LinkDaLive().Match(html);
        if (!live.Success || Reprise().IsMatch(html))
            return null;

        // A continuação do chat da live, não a de outras listas da página
        int chat = html.IndexOf("\"liveChatRenderer\"", StringComparison.Ordinal);
        Match continuacao = chat < 0 ? Match.Empty : ContinuacaoDoChat().Match(html, chat);
        if (!continuacao.Success)
            return null;

        Match chave = ChaveDaApi().Match(html);
        Match versao = VersaoDoSite().Match(html);
        if (!chave.Success || !versao.Success)
            throw new FormatException("A página da live do YouTube veio sem a chave ou a versão da API.");
        return new PaginaDaLive(live.Groups[1].Value, chave.Groups[1].Value, versao.Groups[1].Value, continuacao.Groups[1].Value);
    }

    /// <summary>O corpo do pedido das mensagens a partir de uma continuação (a primeira é a da página).</summary>
    public string PedidoDoChat(string continuacao) => JsonSerializer.Serialize(new
    {
        context = new { client = new { clientName = "WEB", clientVersion = VersaoDoCliente } },
        continuation = continuacao,
    });
}
