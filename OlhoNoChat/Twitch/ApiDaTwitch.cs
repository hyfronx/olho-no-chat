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

    /// <summary>Assina um evento numa sessão do EventSub por websocket (202 = assinado).</summary>
    public Task<Resposta> AssinarAsync(string token, string tipo, string versao, IReadOnlyDictionary<string, string> condicao,
        string idDaSessao) =>
        MandarAsync(Pedido(HttpMethod.Post, "eventsub/subscriptions", token, new
        {
            type = tipo,
            version = versao,
            condition = condicao,
            transport = new { method = "websocket", session_id = idDaSessao }
        }));

    /// <summary>
    /// Aprova ou recusa uma mensagem que o AutoMod segurou (204 = feito; 403 = a conta não é moderadora do canal;
    /// 404 = a mensagem não está mais na fila).
    /// </summary>
    public Task<Resposta> DecidirNoAutoModAsync(string token, string idDaConta, string idDaMensagem, bool aprovar) =>
        MandarAsync(Pedido(HttpMethod.Post, "moderation/automod/message", token,
            new { user_id = idDaConta, msg_id = idDaMensagem, action = aprovar ? "ALLOW" : "DENY" }));

    /// <summary>
    /// Os anúncios do canal da conta: quando é o próximo, quanto dura e quantas vezes ainda dá para adiar (só o dono do
    /// canal; os horários vêm em segundos Unix, 0 = nenhum).
    /// </summary>
    public Task<Resposta> AnunciosAsync(string token, string idDaConta) =>
        MandarAsync(Pedido(HttpMethod.Get, "channels/ads?broadcaster_id=" + Uri.EscapeDataString(idDaConta), token));

    /// <summary>
    /// Adia o próximo anúncio em 5 minutos (200 = feito, com o horário novo; 429 = sem adiamentos ou outro pedido em
    /// andamento; 400 = canal offline ou sem anúncio marcado).
    /// </summary>
    public Task<Resposta> AdiarAnuncioAsync(string token, string idDaConta) =>
        MandarAsync(Pedido(HttpMethod.Post, "channels/ads/schedule/snooze?broadcaster_id=" + Uri.EscapeDataString(idDaConta), token));

    /// <summary>
    /// Uma página dos canais que a conta modera (até 100; <paramref name="cursor"/> para a seguinte). O canal da própria
    /// conta não vem: o dono não é moderador.
    /// </summary>
    public Task<Resposta> CanaisModeradosAsync(string token, string idDaConta, string? cursor) =>
        MandarAsync(Pedido(HttpMethod.Get, "moderation/channels?first=100&user_id=" + Uri.EscapeDataString(idDaConta)
            + (cursor != null ? "&after=" + Uri.EscapeDataString(cursor) : string.Empty), token));

    /// <summary>
    /// Apaga uma mensagem do chat (204 = feito; 400 = de moderador ou do dono, ou com mais de 6 horas; 403 = a conta
    /// não modera o canal; 404 = a mensagem não existe mais).
    /// </summary>
    public Task<Resposta> ApagarMensagemAsync(string token, string idDoCanal, string idDaConta, string idDaMensagem) =>
        MandarAsync(Pedido(HttpMethod.Delete, "moderation/chat?broadcaster_id=" + Uri.EscapeDataString(idDoCanal)
            + "&moderator_id=" + Uri.EscapeDataString(idDaConta) + "&message_id=" + Uri.EscapeDataString(idDaMensagem), token));

    /// <summary>
    /// Suspende (com <paramref name="segundos"/>) ou bane (sem) alguém do chat (200 = feito; 400 = não pode, como um
    /// moderador, ou já banido; 403 = a conta não modera o canal; 409 = outro pedido para a mesma pessoa agora).
    /// </summary>
    public Task<Resposta> BanirAsync(string token, string idDoCanal, string idDaConta, string idDoUsuario, int? segundos) =>
        MandarAsync(Pedido(HttpMethod.Post, "moderation/bans?broadcaster_id=" + Uri.EscapeDataString(idDoCanal)
            + "&moderator_id=" + Uri.EscapeDataString(idDaConta), token,
            new { data = segundos is int s ? (object)new { user_id = idDoUsuario, duration = s } : new { user_id = idDoUsuario } }));

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
