using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

/// <summary>Responde no lugar da Twitch: cada rota é "MÉTODO trecho-do-endereço". Sem internet de verdade.</summary>
public sealed class TwitchFalsa : HttpMessageHandler
{
    public sealed record Pedido(HttpMethod Metodo, string Endereco, string Corpo, string Autorizacao, string ClientId);

    private readonly List<(string Metodo, string Trecho, Func<Pedido, (HttpStatusCode, string)> Resposta)> _rotas = [];

    public List<Pedido> Pedidos { get; } = [];

    /// <summary>Sem rota que sirva, a "internet" falha.</summary>
    public TwitchFalsa Quando(string metodo, string trecho, HttpStatusCode codigo, string corpo = "") =>
        Quando(metodo, trecho, _ => (codigo, corpo));

    public TwitchFalsa Quando(string metodo, string trecho, Func<Pedido, (HttpStatusCode, string)> resposta)
    {
        _rotas.Insert(0, (metodo, trecho, resposta)); // a última definida vale
        return this;
    }

    public IReadOnlyList<Pedido> PedidosPara(string trecho)
    {
        lock (Pedidos)
            return Pedidos.Where(p => p.Endereco.Contains(trecho)).ToList();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelar)
    {
        await Task.Yield(); // como a rede de verdade, a resposta nunca chega na mesma hora
        var registro = new Pedido(pedido.Method, pedido.RequestUri!.ToString(),
            pedido.Content != null ? await pedido.Content.ReadAsStringAsync(cancelar) : "",
            pedido.Headers.Authorization?.ToString() ?? "",
            pedido.Headers.TryGetValues("Client-Id", out var ids) ? ids.First() : "");
        lock (Pedidos)
            Pedidos.Add(registro);

        foreach (var (metodo, trecho, resposta) in _rotas)
        {
            if (metodo == pedido.Method.Method && registro.Endereco.Contains(trecho))
            {
                var (codigo, corpo) = resposta(registro);
                return new HttpResponseMessage(codigo) { Content = new StringContent(corpo) };
            }
        }
        throw new HttpRequestException("Sem internet (nenhuma rota falsa).");
    }
}

public sealed class ContaSalvaNaMemoria : IContaSalva
{
    public string Token { get; set; } = "";
    public string Id { get; set; } = "";
    public string Login { get; set; } = "";
    public string NomeDeExibicao { get; set; } = "";
    public int Gravacoes { get; private set; }
    public void Gravar() => Gravacoes++;
}

public static class Montar
{
    public const string ValidacaoOk = """
        {"client_id":"zrqsilh31pbdlfjb81onhulkvzytgh","login":"hyfronx","scopes":["user:write:chat","channel:read:redemptions","user:read:emotes"],"user_id":"121292674","expires_in":5000000}
        """;

    public const string PerfilOk = """
        {"data":[{"id":"121292674","login":"hyfronx","display_name":"Hyfronx","profile_image_url":"https://exemplo/foto.png"}]}
        """;

    public static ContaSalvaNaMemoria ContaConectada() =>
        new() { Token = "tok", Id = "121292674", Login = "hyfronx", NomeDeExibicao = "Hyfronx" };

    public static ContaDaTwitch Conta(TwitchFalsa twitch, IContaSalva salva) =>
        new(new ApiDaTwitch(twitch), salva, NullLogger<ContaDaTwitch>.Instance);
}
