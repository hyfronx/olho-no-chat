using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OlhoNoChat.Kick;

/// <summary>Os pedidos que o leitor faz à Kick (os testes usam um falso). Erros de HTTP saem como <see cref="HttpRequestException"/>.</summary>
public interface IConexaoComAKick
{
    /// <summary>O JSON da <see cref="SalaDaKick"/> do canal, ou null quando a Kick diz que o canal não existe (404).</summary>
    Task<string?> CanalAsync(string canal, CancellationToken cancelar);

    /// <summary>Um token de convidado do tempo real para este cliente (vale uns 45 minutos).</summary>
    Task<string> TokenAsync(string idDoCliente, CancellationToken cancelar);

    /// <summary>O endereço do WebSocket do Centrifugo para este cliente (muda por região: nunca fixo).</summary>
    Task<string> EnderecoAsync(long idDoCanal, string idDoCliente, CancellationToken cancelar);

    /// <summary>Abre o WebSocket do tempo real (sem conectar nem assinar a sala ainda: ver <see cref="QuadrosDaKick"/>).</summary>
    Task<ISocketDaKick> AbrirAsync(string endereco, CancellationToken cancelar);
}

/// <summary>O WebSocket do tempo real da Kick, em texto. Um envio de cada vez.</summary>
public interface ISocketDaKick : IDisposable
{
    /// <summary>O próximo quadro, ou null quando a Kick fechou a conexão (o motivo fica em <see cref="Fechamento"/>).</summary>
    Task<string?> ReceberAsync(CancellationToken cancelar);

    Task EnviarAsync(string quadro, CancellationToken cancelar);

    /// <summary>O código e o motivo que a Kick deu ao fechar, para o log ("" enquanto aberto).</summary>
    string Fechamento { get; }
}

/// <summary>
/// Os pedidos de verdade, como o site da Kick faz para quem assiste sem conta: o canal pela API do site, um token de
/// convidado e o endereço do Centrifugo pelo <c>web.kick.com</c>, e o WebSocket. Nada disso pede login. Fica aqui, e não
/// na página do chat, porque o <c>web.kick.com</c> não responde a outra origem (CORS) e o Centrifugo recusa o WebSocket
/// com a origem da página; sem origem nenhuma (como aqui) ele aceita. Nenhum cabeçalho finge ser o site.
/// </summary>
public sealed class ConexaoComAKick : IConexaoComAKick
{
    private const string TempoReal = "https://web.kick.com/api/v1/realtime/";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<string?> CanalAsync(string canal, CancellationToken cancelar)
    {
        using HttpResponseMessage resposta = await Http.GetAsync(SalaDaKick.Endereco(canal), cancelar).ConfigureAwait(false);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
            return null;
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }

    public async Task<string> TokenAsync(string idDoCliente, CancellationToken cancelar) =>
        LerToken(await PostarAsync("auth/connection", new { client_id = idDoCliente }, cancelar).ConfigureAwait(false));

    public async Task<string> EnderecoAsync(long idDoCanal, string idDoCliente, CancellationToken cancelar) =>
        LerEndereco(await PostarAsync($"channels/{idDoCanal}/chat/connection", new
        {
            client = new { id = idDoCliente, type = "web" },
            capabilities = new { accepted_providers = new[] { new { provider = "centrifugo" } } },
        }, cancelar).ConfigureAwait(false));

    public async Task<ISocketDaKick> AbrirAsync(string endereco, CancellationToken cancelar)
    {
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(new Uri(endereco), cancelar).ConfigureAwait(false);
            return new SocketDaKick(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>O token na resposta do <c>auth/connection</c>: <c>{"data":{"token":"..."}}</c>.</summary>
    /// <exception cref="JsonException">A resposta não é a esperada.</exception>
    public static string LerToken(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        return documento.RootElement.TryGetProperty("data", out JsonElement dados) && dados.ValueKind == JsonValueKind.Object
               && dados.TryGetProperty("token", out JsonElement token) && token.ValueKind == JsonValueKind.String
               && token.GetString() is { Length: > 0 } texto
            ? texto
            : throw new JsonException("A Kick não deu o token do tempo real.");
    }

    /// <summary>
    /// O endereço do Centrifugo na resposta do <c>chat/connection</c>:
    /// <c>{"data":{"connections":[{"provider":"centrifugo","credentials":{"url":"wss://..."}}]}}</c>.
    /// </summary>
    /// <exception cref="JsonException">Sem o Centrifugo na resposta (a Kick mudou de provedor ou de formato).</exception>
    public static string LerEndereco(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        if (documento.RootElement.TryGetProperty("data", out JsonElement dados) && dados.ValueKind == JsonValueKind.Object
            && dados.TryGetProperty("connections", out JsonElement conexoes) && conexoes.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement conexao in conexoes.EnumerateArray())
            {
                if (conexao.ValueKind == JsonValueKind.Object
                    && conexao.TryGetProperty("provider", out JsonElement provedor) && provedor.ValueKind == JsonValueKind.String
                    && provedor.GetString() == "centrifugo"
                    && conexao.TryGetProperty("credentials", out JsonElement credenciais) && credenciais.ValueKind == JsonValueKind.Object
                    && credenciais.TryGetProperty("url", out JsonElement url) && url.ValueKind == JsonValueKind.String
                    && url.GetString() is { } endereco && endereco.StartsWith("wss://", StringComparison.Ordinal))
                    return endereco;
            }
        }
        throw new JsonException("A Kick não deu o endereço do Centrifugo.");
    }

    private static async Task<string> PostarAsync(string caminho, object corpo, CancellationToken cancelar)
    {
        using var conteudo = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        using HttpResponseMessage resposta = await Http.PostAsync(TempoReal + caminho, conteudo, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }

    private sealed class SocketDaKick(ClientWebSocket socket) : ISocketDaKick
    {
        private readonly byte[] _buffer = new byte[64 * 1024];

        public string Fechamento { get; private set; } = string.Empty;

        public async Task<string?> ReceberAsync(CancellationToken cancelar)
        {
            using var quadro = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult parte = await socket.ReceiveAsync(_buffer, cancelar).ConfigureAwait(false);
                if (parte.MessageType == WebSocketMessageType.Close)
                {
                    Fechamento = $"{(int?)socket.CloseStatus} {socket.CloseStatusDescription}".Trim();
                    return null;
                }
                quadro.Write(_buffer, 0, parte.Count);
                if (parte.EndOfMessage)
                    return Encoding.UTF8.GetString(quadro.GetBuffer(), 0, (int)quadro.Length);
            }
        }

        public Task EnviarAsync(string quadro, CancellationToken cancelar) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(quadro), WebSocketMessageType.Text, endOfMessage: true, cancelar);

        // Sem esperar o "tchau" da Kick: a leitura já acabou
        public void Dispose()
        {
            socket.Abort();
            socket.Dispose();
        }
    }
}
