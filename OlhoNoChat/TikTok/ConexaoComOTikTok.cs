using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;

namespace OlhoNoChat.TikTok;

/// <summary>Os pedidos que o leitor faz ao TikTok (os testes usam um falso). Erros de HTTP saem como <see cref="HttpRequestException"/>.</summary>
public interface IConexaoComOTikTok
{
    /// <summary>O JSON do <see cref="SalaDoTikTok"/> do canal.</summary>
    Task<string> SalaAsync(string canal, CancellationToken cancelar);

    /// <summary>Abre o WebSocket do chat da sala (sem entrar nela ainda: ver <see cref="QuadrosDoTikTok.EntrarNaSala"/>).</summary>
    Task<ISocketDoTikTok> AbrirAsync(string idDaSala, CancellationToken cancelar);

    /// <summary>O JSON da <see cref="ListaDePresentes"/> da sala (em português).</summary>
    Task<string> PresentesAsync(string idDaSala, CancellationToken cancelar);

    /// <summary>Lê com a conta do TikTok conectada (a aba TikTok).</summary>
    bool ComSessao => false;

    /// <summary>Se o TikTok ainda aceita a sessão da conta conectada (sem conta: <see cref="SituacaoDaSessao.Valida"/>).</summary>
    Task<SituacaoDaSessao> ConferirSessaoAsync(CancellationToken cancelar) => Task.FromResult(SituacaoDaSessao.Valida);
}

/// <summary>O que o TikTok disse da sessão da conta conectada.</summary>
public enum SituacaoDaSessao
{
    Valida,

    /// <summary>O TikTok não aceita mais a sessão (saiu da conta, trocou a senha, venceu).</summary>
    Recusada,

    /// <summary>Sem resposta (sem internet, TikTok fora do ar): não dá para saber.</summary>
    SemResposta,
}

/// <summary>O WebSocket do chat de uma live. Um envio de cada vez.</summary>
public interface ISocketDoTikTok : IDisposable
{
    /// <summary>O próximo quadro, ou null quando o TikTok fechou a conexão.</summary>
    Task<byte[]?> ReceberAsync(CancellationToken cancelar);

    Task EnviarAsync(byte[] quadro, CancellationToken cancelar);
}

/// <summary>
/// Os pedidos de verdade, como o site do TikTok faz para quem assiste sem conta: um cookie de visitante ("ttwid", que o
/// TikTok dá na página das lives), a sala pelo <c>api-live/user/room</c> e o WebSocket do chat com esse cookie. Nada
/// disso pede login nem assinatura. Com a conta do TikTok conectada (opcional), a sessão dela vai só no WebSocket. O WebSocket fica aqui, e não na página do chat, porque o navegador não deixa pôr o
/// cookie num WebSocket para outro site.
/// </summary>
public sealed class ConexaoComOTikTok : IConexaoComOTikTok
{
    public const string Navegador = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    // O endereço que o site usa com "ws_direct": não pede a assinatura dos outros endereços do chat. O segundo é a reserva.
    private static readonly string[] Servidores = ["webcast-ws.tiktok.com", "webcast-ws.us.tiktok.com"];
    private const string CaminhoDoChat = "/webcast/im/ws_proxy/ws_reuse_supplement/";

    /// <summary>De quanto em quanto tempo o leitor manda o "estou aqui" (vai no endereço do WebSocket).</summary>
    public static readonly TimeSpan IntervaloDasBatidas = TimeSpan.FromSeconds(10);

    // O TikTok às vezes não dá o cookie: quantas vezes pedir, e a pausa entre um pedido e outro
    private const int TentativasDoCookie = 10;
    private static readonly TimeSpan EntreTentativas = TimeSpan.FromMilliseconds(300);

    // Sem guardar cookies: o ttwid vai à mão só no WebSocket
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        BaseAddress = new Uri("https://www.tiktok.com"),
        Timeout = TimeSpan.FromSeconds(20),
    };

    // Só para a conferência da sessão: sem seguir redirecionamentos, que poderiam levar o cookie para outro endereço
    private static readonly HttpClient HttpDaSessao = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        BaseAddress = new Uri("https://www.tiktok.com"),
        Timeout = TimeSpan.FromSeconds(20),
    };

    private string? _ttwid;
    private readonly Func<string?> _sessao;

    /// <param name="sessao">A sessão da conta conectada na hora de conectar, ou null para ler sem conta.</param>
    public ConexaoComOTikTok(Func<string?>? sessao = null) => _sessao = sessao ?? (() => null);

    public bool ComSessao => _sessao() != null;

    public async Task<SituacaoDaSessao> ConferirSessaoAsync(CancellationToken cancelar) =>
        _sessao() is { } sessao ? (await ConferirAsync(sessao, cancelar).ConfigureAwait(false)).Situacao : SituacaoDaSessao.Valida;

    /// <summary>
    /// Confere uma sessão com o TikTok (o mesmo pedido que o site faz para saber quem está logado) e devolve o @ e o
    /// id da conta. A sessão só vai no cabeçalho, para o TikTok, por HTTPS.
    /// </summary>
    public static async Task<(SituacaoDaSessao Situacao, string Login, string Id)> ConferirAsync(string sessao, CancellationToken cancelar)
    {
        try
        {
            using HttpRequestMessage pedido = Pedido("/passport/web/account/info/?aid=1988&app_name=tiktok_web&device_platform=web_pc");
            pedido.Headers.Add("Cookie", "sessionid=" + sessao);
            using HttpResponseMessage resposta = await HttpDaSessao.SendAsync(pedido, cancelar).ConfigureAwait(false);
            if (!resposta.IsSuccessStatusCode)
                return (SituacaoDaSessao.SemResposta, "", "");
            using var json = System.Text.Json.JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false));
            System.Text.Json.JsonElement raiz = json.RootElement;
            if (raiz.TryGetProperty("data", out var dados) && dados.ValueKind == System.Text.Json.JsonValueKind.Object
                && dados.TryGetProperty("username", out var nome) && nome.ValueKind == System.Text.Json.JsonValueKind.String
                && nome.GetString() is { Length: > 0 } login)
            {
                string id = dados.TryGetProperty("user_id_str", out var i) && i.ValueKind == System.Text.Json.JsonValueKind.String ? i.GetString()! : "";
                return (SituacaoDaSessao.Valida, login.ToLowerInvariant(), id);
            }
            return (SituacaoDaSessao.Recusada, "", "");
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException
                                      or TaskCanceledException && !cancelar.IsCancellationRequested)
        {
            return (SituacaoDaSessao.SemResposta, "", "");
        }
    }

    public async Task<string> SalaAsync(string canal, CancellationToken cancelar)
    {
        using HttpRequestMessage pedido = Pedido(SalaDoTikTok.Caminho(canal));
        using HttpResponseMessage resposta = await Http.SendAsync(pedido, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }

    public async Task<string> PresentesAsync(string idDaSala, CancellationToken cancelar)
    {
        using HttpRequestMessage pedido = Pedido(ListaDePresentes.Endereco(idDaSala));
        using HttpResponseMessage resposta = await Http.SendAsync(pedido, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }

    public async Task<ISocketDoTikTok> AbrirAsync(string idDaSala, CancellationToken cancelar)
    {
        // Um cookie velho é o motivo mais comum de o TikTok recusar: tenta mais uma vez com um novo
        try
        {
            return await AbrirComOCookieAsync(idDaSala, await CookieAsync(novo: false, cancelar).ConfigureAwait(false), cancelar).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            return await AbrirComOCookieAsync(idDaSala, await CookieAsync(novo: true, cancelar).ConfigureAwait(false), cancelar).ConfigureAwait(false);
        }
    }

    private async Task<ISocketDoTikTok> AbrirComOCookieAsync(string idDaSala, string ttwid, CancellationToken cancelar)
    {
        WebSocketException? erro = null;
        foreach (string servidor in Servidores)
        {
            var socket = new ClientWebSocket();
            // Com a conta conectada, a sessão vai junto: o TikTok manda também as mensagens que só mostra para quem
            // está logado
            string? sessao = _sessao();
            socket.Options.SetRequestHeader("Cookie", "ttwid=" + ttwid + (sessao != null ? "; sessionid=" + sessao : ""));
            socket.Options.SetRequestHeader("Origin", "https://www.tiktok.com");
            socket.Options.SetRequestHeader("User-Agent", Navegador);
            try
            {
                await socket.ConnectAsync(new Uri($"wss://{servidor}{CaminhoDoChat}?{Parametros(idDaSala)}"), cancelar).ConfigureAwait(false);
                return new SocketDoTikTok(socket);
            }
            catch (WebSocketException e)
            {
                socket.Dispose();
                erro = e;
            }
        }
        throw erro!;
    }

    // O cookie de visitante: vem da página das lives. A página inicial também dava, mas não sempre, e em 2026-10-26
    // parou de vez (vem com o cabeçalho "X-TT-System-Error: 3"). Por garantia, pede de novo algumas vezes
    private async Task<string> CookieAsync(bool novo, CancellationToken cancelar)
    {
        if (_ttwid is { } guardado && !novo)
            return guardado;
        for (int tentativa = 1; ; tentativa++)
        {
            using HttpRequestMessage pedido = Pedido("/live");
            using HttpResponseMessage resposta = await Http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cancelar).ConfigureAwait(false);
            resposta.EnsureSuccessStatusCode();
            string? ttwid = resposta.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
                ? cookies.Select(c => c.Split(';')[0].Trim()).FirstOrDefault(c => c.StartsWith("ttwid=", StringComparison.Ordinal))?["ttwid=".Length..]
                : null;
            if (!string.IsNullOrEmpty(ttwid))
                return _ttwid = ttwid;
            if (tentativa == TentativasDoCookie)
                throw new HttpRequestException("O TikTok não deu o cookie de visitante.");
            await Task.Delay(EntreTentativas, cancelar).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage Pedido(string caminho)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
        pedido.Headers.Add("User-Agent", Navegador);
        pedido.Headers.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
        pedido.Headers.Add("Referer", "https://www.tiktok.com/");
        return pedido;
    }

    // Os mesmos que o site manda (como espectador, mensagens em protobuf com gzip)
    private static string Parametros(string idDaSala)
    {
        string fuso = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out string? iana) ? iana : "America/Sao_Paulo";
        var parametros = new (string, string)[]
        {
            ("version_code", "180800"), ("device_platform", "web"), ("cookie_enabled", "true"),
            ("screen_width", "1920"), ("screen_height", "1080"), ("browser_language", "pt-BR"), ("browser_platform", "Win32"),
            ("browser_name", "Mozilla"), ("browser_version", Navegador["Mozilla/".Length..]), ("browser_online", "true"),
            ("tz_name", fuso), ("app_name", "tiktok_web"), ("sup_ws_ds_opt", "1"), ("update_version_code", "2.0.0"),
            ("compress", "gzip"), ("webcast_language", "pt-BR"), ("ws_direct", "1"), ("aid", "1988"), ("live_id", "12"),
            ("app_language", "pt-BR"), ("client_enter", "1"), ("room_id", idDaSala), ("identity", "audience"),
            ("history_comment_count", "6"), ("heartbeat_duration", ((int)IntervaloDasBatidas.TotalMilliseconds).ToString()),
            ("resp_content_type", "protobuf"), ("did_rule", "3"),
        };
        return string.Join("&", parametros.Select(p => p.Item1 + "=" + Uri.EscapeDataString(p.Item2)));
    }

    private sealed class SocketDoTikTok(ClientWebSocket socket) : ISocketDoTikTok
    {
        private readonly byte[] _buffer = new byte[64 * 1024];

        public async Task<byte[]?> ReceberAsync(CancellationToken cancelar)
        {
            using var quadro = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult parte = await socket.ReceiveAsync(_buffer, cancelar).ConfigureAwait(false);
                if (parte.MessageType == WebSocketMessageType.Close)
                    return null;
                quadro.Write(_buffer, 0, parte.Count);
                if (parte.EndOfMessage)
                    return quadro.ToArray();
            }
        }

        public Task EnviarAsync(byte[] quadro, CancellationToken cancelar) =>
            socket.SendAsync(quadro, WebSocketMessageType.Binary, endOfMessage: true, cancelar);

        // Sem esperar o "tchau" do TikTok: a leitura já acabou
        public void Dispose()
        {
            socket.Abort();
            socket.Dispose();
        }
    }
}
