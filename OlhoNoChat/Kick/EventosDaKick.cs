using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OlhoNoChat.Kick;

/// <summary>Um pedaço do texto de uma mensagem: texto, ou um emote da Kick (a imagem e o nome).</summary>
public sealed record ParteDaKick(string Texto, string? Imagem = null);

/// <summary>
/// Um selo ao lado do nome, na ordem do site da Kick: um da Twitch que a página já tem (<see cref="DaTwitch"/>, como
/// "moderator/1") ou uma imagem da Kick (os selos novos e a insígnia de assinante do canal).
/// </summary>
public sealed record SeloDaKick(string? DaTwitch, string? Imagem, string Nome);

/// <summary>Uma mensagem do chat da Kick. <see cref="Papeis"/>: "broadcaster", "moderator" e "vip" (valem para os filtros).</summary>
public sealed record ItemDaKick(string Id, string Login, string Nome, string Cor, IReadOnlyList<ParteDaKick> Partes,
    IReadOnlyList<string> Papeis, IReadOnlyList<SeloDaKick> Selos);

public enum TipoDeEventoDaKick
{
    Mensagem,

    /// <summary>Um moderador apagou uma mensagem (<see cref="EventoDaKick.Id"/>).</summary>
    Apagada,

    /// <summary>Alguém foi banido ou suspenso: as mensagens dele saem (<see cref="EventoDaKick.Login"/>).</summary>
    Banido,

    /// <summary>Um moderador limpou o chat.</summary>
    Limpeza,
}

public sealed record EventoDaKick(TipoDeEventoDaKick Tipo, ItemDaKick? Mensagem = null, string Id = "", string Login = "");

/// <summary>
/// Os eventos da sala do chat que importam para a tela (o resto, como enquetes e mensagens fixadas, é ignorado). O
/// <c>data</c> de cada um é o mesmo que o Pusher mandava: <c>content</c> com os emotes como <c>[emote:id:nome]</c>,
/// <c>sender.username</c>, <c>sender.identity.color</c>, os selos antigos em <c>identity.badges</c> e os novos em
/// <c>identity.badges_v2</c>.
/// </summary>
public static partial class EventosDaKick
{
    public const string Mensagem = @"App\Events\ChatMessageEvent";
    public const string Apagada = @"App\Events\MessageDeletedEvent";
    public const string Banido = @"App\Events\UserBannedEvent";
    public const string Limpeza = @"App\Events\ChatroomClearEvent";

    // Os selos antigos que a página desenha com as imagens da Twitch (e que valem para os filtros)
    private static readonly Dictionary<string, string> SelosDaTwitch = new()
    {
        ["broadcaster"] = "broadcaster/1",
        ["moderator"] = "moderator/1",
        ["vip"] = "vip/1",
    };

    [GeneratedRegex(@"\[emote:(\d+):([^\]]*)\]")]
    private static partial Regex Emote();

    /// <summary>O evento que a tela usa, ou null (outro evento, ou sem o que precisa).</summary>
    /// <param name="sala">O canal aberto, para a insígnia de assinante.</param>
    /// <exception cref="JsonException">O <c>data</c> não é JSON.</exception>
    public static EventoDaKick? Ler(string evento, string dados, SalaDaKick sala)
    {
        if (evento is not (Mensagem or Apagada or Banido or Limpeza))
            return null;
        if (evento == Limpeza)
            return new EventoDaKick(TipoDeEventoDaKick.Limpeza);

        using JsonDocument documento = JsonDocument.Parse(dados);
        JsonElement raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
            return null;
        switch (evento)
        {
            case Apagada:
                return Objeto(raiz, "message") is { } apagada && Texto(apagada, "id") is { Length: > 0 } id
                    ? new EventoDaKick(TipoDeEventoDaKick.Apagada, Id: id)
                    : null;
            case Banido:
                return Objeto(raiz, "user") is { } usuario && Texto(usuario, "username") is { Length: > 0 } nome
                    ? new EventoDaKick(TipoDeEventoDaKick.Banido, Login: nome.ToLowerInvariant())
                    : null;
            default:
                return LerMensagem(raiz, sala) is { } item ? new EventoDaKick(TipoDeEventoDaKick.Mensagem, item) : null;
        }
    }

    private static ItemDaKick? LerMensagem(JsonElement raiz, SalaDaKick sala)
    {
        JsonElement remetente = Objeto(raiz, "sender") ?? default;
        string nome = remetente.ValueKind == JsonValueKind.Object ? Texto(remetente, "username") : string.Empty;
        if (nome.Length == 0 && remetente.ValueKind == JsonValueKind.Object)
            nome = Texto(remetente, "slug");
        string texto = Texto(raiz, "content");
        string id = Texto(raiz, "id");
        if (nome.Length == 0 || texto.Length == 0 || id.Length == 0)
            return null;

        JsonElement identidade = remetente.ValueKind == JsonValueKind.Object ? Objeto(remetente, "identity") ?? default : default;
        (IReadOnlyList<string> papeis, IReadOnlyList<SeloDaKick> selos) = identidade.ValueKind == JsonValueKind.Object
            ? Selos(identidade, sala)
            : ([], []);
        string cor = identidade.ValueKind == JsonValueKind.Object ? Texto(identidade, "color") : string.Empty;
        return new ItemDaKick(id, nome.ToLowerInvariant(), nome, cor, Partes(texto), papeis, selos);
    }

    /// <summary>"boa [emote:37226:KEKW]" vira o texto e o emote (a imagem é a do site da Kick).</summary>
    public static IReadOnlyList<ParteDaKick> Partes(string texto)
    {
        var partes = new List<ParteDaKick>();
        int ultimo = 0;
        foreach (Match emote in Emote().Matches(texto))
        {
            if (emote.Index > ultimo)
                partes.Add(new ParteDaKick(texto[ultimo..emote.Index]));
            partes.Add(new ParteDaKick(emote.Groups[2].Value, $"https://files.kick.com/emotes/{emote.Groups[1].Value}/fullsize"));
            ultimo = emote.Index + emote.Length;
        }
        if (ultimo < texto.Length)
            partes.Add(new ParteDaKick(texto[ultimo..]));
        return partes;
    }

    // Como o site mostra: primeiro os selos novos escolhidos pela pessoa ("selected"), pela ordem da Kick (o de nível
    // antes), depois os antigos (dono, moderador, VIP e a insígnia de assinante do canal). Os outros antigos
    // (fundador, verificado, presenteador...) não aparecem, como antes
    private static (IReadOnlyList<string> Papeis, IReadOnlyList<SeloDaKick> Selos) Selos(JsonElement identidade, SalaDaKick sala)
    {
        var papeis = new List<string>();
        var selos = new List<SeloDaKick>();

        if (identidade.TryGetProperty("badges_v2", out JsonElement novos) && novos.ValueKind == JsonValueKind.Array)
        {
            selos.AddRange(novos.EnumerateArray()
                .Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("selected", out JsonElement escolhido)
                            && escolhido.ValueKind == JsonValueKind.True)
                .Select(s => (Ordem: Inteiro(s, "sort_order"), Selo: SeloNovo(s)))
                .Where(s => s.Selo != null)
                .OrderBy(s => s.Ordem) // estável: os de mesma ordem ficam como vieram
                .Select(s => s.Selo!));
        }

        if (identidade.TryGetProperty("badges", out JsonElement antigos) && antigos.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement selo in antigos.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object).OrderBy(s => Inteiro(s, "sort_order")))
            {
                string tipo = Texto(selo, "type");
                if (SelosDaTwitch.TryGetValue(tipo, out string? daTwitch))
                {
                    papeis.Add(tipo);
                    selos.Add(new SeloDaKick(daTwitch, null, tipo));
                }
                else if (tipo == "subscriber" && sala.InsigniaPara(Inteiro(selo, "count")) is { } imagem)
                {
                    selos.Add(new SeloDaKick(null, imagem, "Assinante"));
                }
            }
        }
        return (papeis, selos);
    }

    // {"name":"level","image_url":"https://ext.cdn.kick.com/chat/badges/27_....png","metadata":{"level":27}} ou
    // {"name":"GoldenK","image_url":"..."}; só imagens do endereço da Kick
    private static SeloDaKick? SeloNovo(JsonElement selo)
    {
        string imagem = Texto(selo, "image_url");
        if (!Uri.TryCreate(imagem, UriKind.Absolute, out Uri? endereco) || endereco.Scheme != Uri.UriSchemeHttps
            || !(endereco.Host == "kick.com" || endereco.Host.EndsWith(".kick.com", StringComparison.Ordinal)))
            return null;
        string nome = Texto(selo, "name");
        if (nome == "level")
        {
            int nivel = Objeto(selo, "metadata") is { } dados ? Inteiro(dados, "level") : 0;
            nome = nivel > 0 ? "Nível " + nivel.ToString(CultureInfo.InvariantCulture) : "Nível";
        }
        return new SeloDaKick(null, imagem, nome);
    }

    private static JsonElement? Objeto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.ValueKind == JsonValueKind.Object ? valor : null;

    private static int Inteiro(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.TryGetInt32(out int n) ? n : 0;

    // Os ids da Kick são texto (uuid); um número também vale
    private static string Texto(JsonElement objeto, string nome) =>
        !objeto.TryGetProperty(nome, out JsonElement valor) ? string.Empty
        : valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty
        : valor.ValueKind == JsonValueKind.Number ? valor.GetRawText()
        : string.Empty;
}
