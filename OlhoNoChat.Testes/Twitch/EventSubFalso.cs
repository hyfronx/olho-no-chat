using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace OlhoNoChat.Testes.Twitch;

/// <summary>Um servidor de websocket local no lugar do EventSub da Twitch: o teste manda as mensagens.</summary>
public sealed class EventSubFalso : IDisposable
{
    private readonly HttpListener _servidor = new();
    private readonly Channel<(WebSocket Ws, string Endereco)> _conexoes = Channel.CreateUnbounded<(WebSocket, string)>();

    public int Porta { get; }

    /// <summary>Aceita e fecha na hora, sem boas-vindas (cada tentativa do app falha rápido).</summary>
    public bool FecharTudo { get; set; }

    /// <summary>Quantas conexões chegaram.</summary>
    public int Conexoes;
    public Uri Endereco => new($"ws://localhost:{Porta}/ws");

    public EventSubFalso()
    {
        var livre = new TcpListener(IPAddress.Loopback, 0);
        livre.Start();
        Porta = ((IPEndPoint)livre.LocalEndpoint).Port;
        livre.Stop();

        _servidor.Prefixes.Add($"http://localhost:{Porta}/");
        _servidor.Start();
        _ = AceitarAsync();
    }

    private async Task AceitarAsync()
    {
        while (_servidor.IsListening)
        {
            try
            {
                HttpListenerContext pedido = await _servidor.GetContextAsync();
                if (!pedido.Request.IsWebSocketRequest)
                {
                    pedido.Response.StatusCode = 400;
                    pedido.Response.Close();
                    continue;
                }
                HttpListenerWebSocketContext ws = await pedido.AcceptWebSocketAsync(null);
                Interlocked.Increment(ref Conexoes);
                if (FecharTudo)
                {
                    // Sem esperar a resposta do app (ele só desiste e fecha)
                    _ = ws.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                        .ContinueWith(t => { _ = t.Exception; ws.WebSocket.Dispose(); });
                    continue;
                }
                await _conexoes.Writer.WriteAsync((ws.WebSocket, pedido.Request.Url!.PathAndQuery));
            }
            catch (Exception) when (!_servidor.IsListening)
            {
                return;
            }
            catch (Exception)
            {
                // Um pedido que deu errado não para o servidor
            }
        }
    }

    public async Task<(WebSocket Ws, string Endereco)> ProximaConexaoAsync()
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _conexoes.Reader.ReadAsync(limite.Token);
    }

    public bool TemConexaoEsperando => _conexoes.Reader.Count > 0;

    public static Task MandarAsync(WebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>Espera o outro lado fechar a conexão (true) ou o tempo acabar (false).</summary>
    public static async Task<bool> FoiFechadaAsync(WebSocket ws, TimeSpan limite)
    {
        using var tempo = new CancellationTokenSource(limite);
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var parte = await ws.ReceiveAsync(buffer, tempo.Token);
                if (parte.MessageType == WebSocketMessageType.Close)
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (WebSocketException)
        {
            return true;
        }
    }

    public static string BoasVindas(string sessao, int silencio = 10) => $$"""
        {"metadata":{"message_id":"bv-{{sessao}}","message_type":"session_welcome","message_timestamp":"2023-07-19T14:56:51.634234626Z"},
         "payload":{"session":{"id":"{{sessao}}","status":"connected","connected_at":"2023-07-19T14:56:51.616329898Z","keepalive_timeout_seconds":{{silencio}},"reconnect_url":null} } }
        """;

    public static string Reconectar(string sessao, string endereco) => $$"""
        {"metadata":{"message_id":"rc-{{sessao}}","message_type":"session_reconnect","message_timestamp":"2022-11-18T09:10:11.634234626Z"},
         "payload":{"session":{"id":"{{sessao}}","status":"reconnecting","keepalive_timeout_seconds":null,"reconnect_url":"{{endereco}}","connected_at":"2022-11-16T10:11:12.634234626Z"} } }
        """;

    public static string EstouVivo(string id) => $$"""
        {"metadata":{"message_id":"{{id}}","message_type":"session_keepalive","message_timestamp":"2023-07-19T10:11:12.634234626Z"},"payload":{} }
        """;

    // O exemplo da documentação da Twitch para channel.channel_points_custom_reward_redemption.add
    public static string Resgate(string id, string nome = "Cooler_User", string titulo = "title", int custo = 100, string texto = "pogchamp") => $$"""
        {"metadata":{"message_id":"{{id}}","message_type":"notification","message_timestamp":"2022-11-16T10:11:12.464757833Z",
                     "subscription_type":"channel.channel_points_custom_reward_redemption.add","subscription_version":"1"},
         "payload":{"subscription":{"id":"f1c2a387-161a-49f9-a165-0f21d7a4e1c4","status":"enabled","type":"channel.channel_points_custom_reward_redemption.add",
                                    "version":"1","condition":{"broadcaster_user_id":"1337"},"transport":{"method":"websocket","session_id":"AQoQexAWVYKSTIu4ec_2VAxyuhAB"},
                                    "created_at":"2022-11-16T10:11:12.464757833Z","cost":0},
                    "event":{"id":"17fa2df1-ad76-4804-bfa5-a40ef63efe63","broadcaster_user_id":"1337","broadcaster_user_login":"cool_user",
                             "broadcaster_user_name":"Cool_User","user_id":"9001","user_login":"cooler_user","user_name":"{{nome}}",
                             "user_input":"{{texto}}","status":"unfulfilled",
                             "reward":{"id":"92af127c-7326-4483-a52b-b0da0be61c01","title":"{{titulo}}","cost":{{custo}},"prompt":"reward prompt"},
                             "redeemed_at":"2020-07-15T17:16:03.17106713Z"} } }
        """;

    public static string Revogacao(string status = "authorization_revoked") => $$"""
        {"metadata":{"message_id":"rv1","message_type":"revocation","message_timestamp":"2022-11-16T10:11:12.464757833Z",
                     "subscription_type":"channel.channel_points_custom_reward_redemption.add","subscription_version":"1"},
         "payload":{"subscription":{"id":"f1c2a387","status":"{{status}}","type":"channel.channel_points_custom_reward_redemption.add","version":"1","cost":0,
                                    "condition":{"broadcaster_user_id":"1337"},"transport":{"method":"websocket","session_id":"x"},"created_at":"2022-11-16T10:11:12.464757833Z"} } }
        """;

    public void Dispose()
    {
        if (_servidor.IsListening)
            _servidor.Stop();
        _servidor.Close();
    }
}
