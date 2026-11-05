using System.Text.Json;

namespace OlhoNoChat.Kick;

/// <summary>O que veio numa linha de um quadro do Centrifugo.</summary>
public enum TipoDeLinhaDaKick
{
    /// <summary>O "ainda está aí?" do servidor (<c>{}</c>): responder <see cref="QuadrosDaKick.Pong"/> logo.</summary>
    Ping,

    /// <summary>A resposta do connect: conectado, com o tempo de vida do token.</summary>
    Conectou,

    /// <summary>A resposta do subscribe: a sala assinada.</summary>
    Assinou,

    /// <summary>A resposta do refresh: o token novo valeu.</summary>
    Renovou,

    /// <summary>Um pedido nosso foi recusado (token inválido, sala recusada...).</summary>
    Erro,

    /// <summary>Um evento da sala (mensagem, apagada, banimento...).</summary>
    Evento,

    /// <summary>O servidor vai desconectar (o motivo vem junto).</summary>
    Desconectar,

    /// <summary>Qualquer outra coisa (ignorada).</summary>
    Outro,
}

/// <summary>Uma linha de um quadro: o tipo e, conforme o tipo, o pedido, o tempo de vida, o código e o texto ou o evento.</summary>
public sealed record LinhaDaKick(TipoDeLinhaDaKick Tipo, int Pedido = 0, int Ttl = 0, int Codigo = 0, string Texto = "",
    string Evento = "", string Dados = "");

/// <summary>
/// O protocolo JSON do Centrifugo como o site da Kick usa (ver <c>PESQUISA-KICK-TEMPO-REAL.md</c>): cada quadro de texto
/// pode ter vários JSON, um por linha, nos dois sentidos. Os pedidos levam um número (<c>id</c>), que volta na resposta.
/// </summary>
public static class QuadrosDaKick
{
    /// <summary>O número do connect e o do subscribe; os refresh contam a partir do seguinte.</summary>
    public const int PedidoConectar = 1, PedidoAssinar = 2;

    /// <summary>A resposta ao ping do servidor. Nunca mandar sem ping (a Kick fecha com 3501).</summary>
    public const string Pong = "{}";

    /// <summary>O canal do Centrifugo da sala do chat.</summary>
    public static string CanalDaSala(long idDaSala) => $"chatrooms.{idDaSala}.v2";

    /// <summary>Conecta com o token de convidado e assina a sala, num quadro só (como o site).</summary>
    public static string ConectarEAssinar(string token, long idDaSala) =>
        JsonSerializer.Serialize(new { id = PedidoConectar, connect = new { token, name = "js" } }) + "\n" +
        JsonSerializer.Serialize(new { id = PedidoAssinar, subscribe = new { channel = CanalDaSala(idDaSala) } });

    /// <summary>Troca o token antes de ele vencer (o novo tem que ser do mesmo cliente).</summary>
    public static string Renovar(int pedido, string token) => JsonSerializer.Serialize(new { id = pedido, refresh = new { token } });

    /// <summary>As linhas de um quadro. Uma linha que não é JSON vira <see cref="TipoDeLinhaDaKick.Outro"/>.</summary>
    public static IReadOnlyList<LinhaDaKick> Ler(string quadro)
    {
        var linhas = new List<LinhaDaKick>();
        foreach (string linha in quadro.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                linhas.Add(LerLinha(linha));
            }
            catch (JsonException)
            {
                linhas.Add(new LinhaDaKick(TipoDeLinhaDaKick.Outro, Texto: linha));
            }
        }
        return linhas;
    }

    private static LinhaDaKick LerLinha(string linha)
    {
        using JsonDocument documento = JsonDocument.Parse(linha);
        JsonElement raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
            return new LinhaDaKick(TipoDeLinhaDaKick.Outro, Texto: linha);
        if (!raiz.EnumerateObject().Any())
            return new LinhaDaKick(TipoDeLinhaDaKick.Ping);

        int pedido = raiz.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int n) ? n : 0;
        if (Objeto(raiz, "error") is { } erro)
            return new LinhaDaKick(TipoDeLinhaDaKick.Erro, pedido, Codigo: Inteiro(erro, "code"), Texto: Texto(erro, "message"));
        if (Objeto(raiz, "connect") is { } conectou)
            return new LinhaDaKick(TipoDeLinhaDaKick.Conectou, pedido, Ttl: Inteiro(conectou, "ttl"));
        if (Objeto(raiz, "subscribe") is not null)
            return new LinhaDaKick(TipoDeLinhaDaKick.Assinou, pedido);
        if (Objeto(raiz, "refresh") is { } renovou)
            return new LinhaDaKick(TipoDeLinhaDaKick.Renovou, pedido, Ttl: Inteiro(renovou, "ttl"));
        if (Objeto(raiz, "disconnect") is { } saiu)
            return new LinhaDaKick(TipoDeLinhaDaKick.Desconectar, Codigo: Inteiro(saiu, "code"), Texto: Texto(saiu, "reason"));

        if (Objeto(raiz, "push") is { } push)
        {
            if (Objeto(push, "disconnect") is { } desconectar)
                return new LinhaDaKick(TipoDeLinhaDaKick.Desconectar, Codigo: Inteiro(desconectar, "code"), Texto: Texto(desconectar, "reason"));
            // {"push":{"channel":"chatrooms.N.v2","pub":{"data":{"event":"App\\Events\\...","data":"<JSON em texto>"}}}}
            if (Objeto(push, "pub") is { } pub && Objeto(pub, "data") is { } dados && Texto(dados, "event") is { Length: > 0 } evento)
            {
                string conteudo = dados.TryGetProperty("data", out JsonElement d)
                    ? d.ValueKind == JsonValueKind.String ? d.GetString() ?? string.Empty : d.GetRawText()
                    : string.Empty;
                return new LinhaDaKick(TipoDeLinhaDaKick.Evento, Evento: evento, Dados: conteudo);
            }
        }
        return new LinhaDaKick(TipoDeLinhaDaKick.Outro, pedido, Texto: linha);
    }

    private static JsonElement? Objeto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.ValueKind == JsonValueKind.Object ? valor : null;

    private static int Inteiro(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.TryGetInt32(out int n) ? n : 0;

    private static string Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty : string.Empty;
}
