#nullable enable
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OlhoNoChat.Twitch;

/// <summary>
/// As chamadas HTTP à Twitch. Cada método devolve a resposta como veio (código e corpo): quem chama decide o que
/// ela quer dizer para a pessoa. Falta de internet e tempo esgotado (15 s) viram exceção.
/// </summary>
public sealed class ApiDaTwitch
{
    private const string Helix = "https://api.twitch.tv/helix/";
    private const string Identidade = "https://id.twitch.tv/oauth2/";

    private readonly HttpClient _http;

    // Login do canal → id, durante a execução (só os que existem)
    private readonly Dictionary<string, string> _idsDosCanais = new(StringComparer.OrdinalIgnoreCase);

    public ApiDaTwitch() : this(new SocketsHttpHandler()) { }

    /// <summary>Com outro <paramref name="transporte"/>, os testes respondem no lugar da Twitch.</summary>
    public ApiDaTwitch(HttpMessageHandler transporte)
    {
        _http = new HttpClient(transporte) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public sealed record Resposta(HttpStatusCode Codigo, string Corpo)
    {
        public bool Deu => (int)Codigo is >= 200 and < 300;

        /// <summary>O corpo como JSON (null se não for JSON).</summary>
        public JsonNode? Json
        {
            get
            {
                try { return JsonNode.Parse(Corpo); }
                catch (JsonException) { return null; }
            }
        }

        /// <summary>A lista "data" das respostas da Helix.</summary>
        public JsonArray Dados => Json?["data"] as JsonArray ?? [];
    }

    /// <summary>Confere um acesso: dono, Client ID e permissões (401 = expirado ou removido).</summary>
    public Task<Resposta> ValidarAsync(string token)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, Identidade + "validate");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        return MandarAsync(pedido);
    }

    /// <summary>O dono do acesso (sem parâmetros) ou os usuários pedidos, por login ou id.</summary>
    public Task<Resposta> UsuariosAsync(string token, string parametros = "") =>
        MandarAsync(Pedido(HttpMethod.Get, "users" + (parametros.Length > 0 ? "?" + parametros : string.Empty), token));

    public Task<Resposta> EnviarMensagemAsync(string token, string idDoCanal, string idDaConta, string texto) =>
        MandarAsync(Pedido(HttpMethod.Post, "chat/messages", token,
            new { broadcaster_id = idDoCanal, sender_id = idDaConta, message = texto }));

    /// <summary>Uma página dos emotes que a conta pode usar (no canal <paramref name="idDoCanal"/>, se houver).</summary>
    public Task<Resposta> EmotesDaContaAsync(string token, string idDaConta, string? idDoCanal, string? cursor) =>
        MandarAsync(Pedido(HttpMethod.Get, "chat/emotes/user?user_id=" + Uri.EscapeDataString(idDaConta)
            + (idDoCanal != null ? "&broadcaster_id=" + Uri.EscapeDataString(idDoCanal) : string.Empty)
            + (cursor != null ? "&after=" + Uri.EscapeDataString(cursor) : string.Empty), token));

    public Task<Resposta> EmotesGlobaisAsync(string token) =>
        MandarAsync(Pedido(HttpMethod.Get, "chat/emotes/global", token));

    /// <summary>Assina os resgates de pontos do canal numa sessão do EventSub por websocket (202 = assinado).</summary>
    public Task<Resposta> AssinarResgatesAsync(string token, string idDoCanal, string idDaSessao) =>
        MandarAsync(Pedido(HttpMethod.Post, "eventsub/subscriptions", token, new
        {
            type = "channel.channel_points_custom_reward_redemption.add",
            version = "1",
            condition = new { broadcaster_user_id = idDoCanal },
            transport = new { method = "websocket", session_id = idDaSessao }
        }));

    /// <summary>Cancela um acesso na Twitch.</summary>
    public Task<Resposta> RevogarAsync(string token) =>
        MandarAsync(new HttpRequestMessage(HttpMethod.Post, Identidade + "revoke")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = InfoDoApp.TwitchClientId,
                ["token"] = token
            })
        });

    /// <summary>
    /// O id de um canal pelo login; null se não existe. Guardado durante a execução. Uma resposta de erro vira
    /// exceção, como a falta de internet (não deu para saber).
    /// </summary>
    public async Task<string?> IdDoCanalAsync(string token, string login)
    {
        if (_idsDosCanais.TryGetValue(login, out string? guardado))
            return guardado;

        Resposta resposta = await UsuariosAsync(token, "login=" + Uri.EscapeDataString(login));
        if (!resposta.Deu)
            throw new HttpRequestException($"A Twitch respondeu {(int)resposta.Codigo} ao procurar o canal.", null, resposta.Codigo);

        string? id = (string?)resposta.Dados.FirstOrDefault()?["id"];
        if (!string.IsNullOrEmpty(id))
            _idsDosCanais[login] = id;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static HttpRequestMessage Pedido(HttpMethod metodo, string caminho, string token, object? corpo = null)
    {
        var pedido = new HttpRequestMessage(metodo, Helix + caminho);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        pedido.Headers.Add("Client-Id", InfoDoApp.TwitchClientId);
        if (corpo != null)
            pedido.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        return pedido;
    }

    private async Task<Resposta> MandarAsync(HttpRequestMessage pedido)
    {
        using (pedido)
        using (HttpResponseMessage resposta = await _http.SendAsync(pedido))
            return new Resposta(resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
    }
}
