using System.Net;
using System.Net.Http;
using System.Text;

namespace OlhoNoChat.YouTube;

/// <summary>Os dois pedidos que o leitor faz ao YouTube (os testes usam um falso). Erros de HTTP saem como <see cref="HttpRequestException"/>.</summary>
public interface IConexaoComOYouTube
{
    /// <summary>O HTML de uma página do site (ver <see cref="PaginaDaLive.Caminho"/>).</summary>
    Task<string> PaginaAsync(string caminho, CancellationToken cancelar);

    /// <summary>O JSON do get_live_chat para o corpo de <see cref="PaginaDaLive.PedidoDoChat"/>.</summary>
    Task<string> ChatAsync(string chave, string corpo, CancellationToken cancelar);
}

/// <summary>Os pedidos de verdade, num só HttpClient para o app todo.</summary>
public sealed class ConexaoComOYouTube : IConexaoComOYouTube
{
    // Sem guardar cookies: cada procura é como a primeira
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        BaseAddress = new Uri("https://www.youtube.com"),
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<string> PaginaAsync(string caminho, CancellationToken cancelar)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
        // Na Europa o YouTube mostra antes o aviso de cookies; este cookie é o "Recusar tudo"
        pedido.Headers.Add("Cookie", "SOCS=CAI");
        using HttpResponseMessage resposta = await Http.SendAsync(pedido, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }

    public async Task<string> ChatAsync(string chave, string corpo, CancellationToken cancelar)
    {
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");
        using HttpResponseMessage resposta = await Http.PostAsync(
            "/youtubei/v1/live_chat/get_live_chat?prettyPrint=false&key=" + Uri.EscapeDataString(chave), conteudo, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }
}
