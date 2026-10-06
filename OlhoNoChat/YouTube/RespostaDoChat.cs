using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OlhoNoChat.YouTube;

/// <summary>
/// Uma resposta do get_live_chat da API interna do YouTube (InnerTube): as mensagens novas, a continuação para o próximo
/// pedido (null quando a live acabou) e, se o YouTube pedir, quanto esperar até ele. Só lê as mensagens adicionadas
/// (addChatItemAction) de texto, Super Chat, Super Sticker e membros; o resto (apagadas, avisos fixados...) fica de fora.
/// </summary>
public sealed partial record RespostaDoChat(IReadOnlyList<ItemDoChat> Itens, string? Continuacao, TimeSpan? Espera)
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex Numero();

    public static RespostaDoChat Ler(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        if (!documento.RootElement.TryGetProperty("continuationContents", out JsonElement conteudo)
            || !conteudo.TryGetProperty("liveChatContinuation", out JsonElement chat))
            return new RespostaDoChat([], null, null);

        var itens = new List<ItemDoChat>();
        foreach (JsonElement acao in Lista(chat, "actions"))
        {
            if (Caminho(acao, "addChatItemAction", "item") is { ValueKind: JsonValueKind.Object } item
                && item.EnumerateObject().FirstOrDefault() is { Value.ValueKind: JsonValueKind.Object } renderer
                && ItemDe(renderer.Name, renderer.Value) is { } lido)
                itens.Add(lido);
        }

        // A continuação vem num "invalidationContinuationData", "timedContinuationData"... Só a "timed" pede uma espera:
        // a das outras é para quem recebe as mensagens empurradas pelo YouTube, não para quem pergunta
        string? continuacao = null;
        TimeSpan? espera = null;
        foreach (JsonProperty dados in Lista(chat, "continuations").Where(c => c.ValueKind == JsonValueKind.Object).SelectMany(c => c.EnumerateObject()))
        {
            if (Texto(dados.Value, "continuation") is not { Length: > 0 } c)
                continue;
            continuacao = c;
            if (dados.Name == "timedContinuationData" && dados.Value.TryGetProperty("timeoutMs", out JsonElement ms)
                && ms.TryGetInt32(out int milissegundos))
                espera = TimeSpan.FromMilliseconds(milissegundos);
            break;
        }
        return new RespostaDoChat(itens, continuacao, espera);
    }

    private static ItemDoChat? ItemDe(string tipo, JsonElement r)
    {
        IReadOnlyList<ParteDoChat> mensagem = Partes(r, "message");
        SuperChatDoChat? superChat = null;
        EventoDeMembro? membro = null;
        JsonElement autor = r; // nome e selos

        switch (tipo)
        {
            case "liveChatTextMessageRenderer":
                break;
            case "liveChatPaidMessageRenderer":
                superChat = new SuperChatDoChat(TextoDe(r, "purchaseAmountText"), Cor(r, "bodyBackgroundColor"));
                break;
            case "liveChatPaidStickerRenderer":
                superChat = new SuperChatDoChat(TextoDe(r, "purchaseAmountText"), Cor(r, "backgroundColor"));
                break;
            case "liveChatMembershipItemRenderer":
                // O marco tem "Membro há 12 meses" em cima; quem virou membro ou subiu de nível, só o texto de baixo
                string emCima = TextoDe(r, "headerPrimaryText");
                if (emCima.Length > 0)
                    membro = new EventoDeMembro(TipoDeEventoDeMembro.Marco, Meses: PrimeiroNumero(emCima));
                else
                    membro = new EventoDeMembro(TextoDe(r, "headerSubtext").StartsWith("Upgraded", StringComparison.OrdinalIgnoreCase)
                        ? TipoDeEventoDeMembro.Subiu
                        : TipoDeEventoDeMembro.Novo);
                if (mensagem.Count == 0)
                    mensagem = Partes(r, "headerSubtext");
                break;
            case "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer":
                // Quem deu está no cabeçalho ("Enviou 5 assinaturas de presente")
                if (Caminho(r, "header", "liveChatSponsorshipsHeaderRenderer") is { } cabecalho)
                    autor = cabecalho;
                membro = new EventoDeMembro(TipoDeEventoDeMembro.DeuPresente, Presentes: PrimeiroNumero(TextoDe(autor, "primaryText")) ?? 1);
                break;
            case "liveChatSponsorshipsGiftRedemptionAnnouncementRenderer":
                // "ganhou uma assinatura de presente de @fulano": quem deu é o último texto
                string? quemDeu = mensagem.LastOrDefault(p => p.Imagem == null)?.Texto.Trim();
                membro = new EventoDeMembro(TipoDeEventoDeMembro.GanhouPresente, QuemDeu: quemDeu);
                break;
            default:
                return null;
        }

        PapelNoChat papel = Papel(autor);
        if (membro != null && papel < PapelNoChat.Membro)
            papel = PapelNoChat.Membro;
        return new ItemDoChat(Texto(r, "id") ?? Guid.NewGuid().ToString(), EnviadaEm(r), TextoDe(autor, "authorName"), papel,
            mensagem, superChat, membro);
    }

    // Os selos: o de membro tem imagem própria do canal; dono e moderador, um ícone
    private static PapelNoChat Papel(JsonElement autor)
    {
        PapelNoChat papel = PapelNoChat.Nenhum;
        foreach (JsonElement selo in Lista(autor, "authorBadges"))
        {
            if (!selo.TryGetProperty("liveChatAuthorBadgeRenderer", out JsonElement s))
                continue;
            PapelNoChat deste = s.TryGetProperty("customThumbnail", out _) ? PapelNoChat.Membro
                : Texto(Caminho(s, "icon") ?? default, "iconType") switch
                {
                    "OWNER" => PapelNoChat.Dono,
                    "MODERATOR" => PapelNoChat.Moderador,
                    _ => PapelNoChat.Nenhum,
                };
            if (deste > papel)
                papel = deste;
        }
        return papel;
    }

    // Textos juntos; emoji comum é o próprio caractere; o do canal, ":nome:" com a imagem
    private static List<ParteDoChat> Partes(JsonElement r, string campo)
    {
        var partes = new List<ParteDoChat>();
        foreach (JsonElement pedaco in Lista(Caminho(r, campo) ?? default, "runs"))
        {
            if (Texto(pedaco, "text") is { } texto)
                partes.Add(new ParteDoChat(texto));
            else if (pedaco.TryGetProperty("emoji", out JsonElement emoji))
            {
                string? imagem = Lista(Caminho(emoji, "image") ?? default, "thumbnails").Select(t => Texto(t, "url")).LastOrDefault(u => u != null);
                bool doCanal = emoji.TryGetProperty("isCustomEmoji", out JsonElement c) && c.ValueKind == JsonValueKind.True;
                string nome = Lista(emoji, "shortcuts").Concat(Lista(emoji, "searchTerms")).Select(t => t.GetString()).FirstOrDefault()
                    ?? $"[:{Texto(emoji, "emojiId")}:]";
                if (!doCanal && Texto(emoji, "emojiId") is { Length: > 0 } caractere)
                    partes.Add(new ParteDoChat(caractere));
                else if (imagem != null)
                    partes.Add(new ParteDoChat(nome, imagem));
            }
        }
        return partes;
    }

    private static DateTimeOffset EnviadaEm(JsonElement r) =>
        long.TryParse(Texto(r, "timestampUsec"), out long microssegundos)
            ? DateTimeOffset.FromUnixTimeMilliseconds(microssegundos / 1000)
            : DateTimeOffset.UtcNow;

    // As cores vêm como um número ARGB; a página quer RRGGBB
    private static string? Cor(JsonElement r, string campo) =>
        r.TryGetProperty(campo, out JsonElement cor) && cor.TryGetInt64(out long argb) ? (argb & 0xFFFFFF).ToString("X6") : null;

    private static int? PrimeiroNumero(string texto) =>
        Numero().Match(texto) is { Success: true } m && int.TryParse(m.Value, out int n) ? n : null;

    // Um texto do YouTube: { "simpleText": "..." } ou { "runs": [{ "text": "..." }, ...] }
    private static string TextoDe(JsonElement r, string campo)
    {
        if (Caminho(r, campo) is not { } t)
            return string.Empty;
        if (Texto(t, "simpleText") is { } simples)
            return simples;
        var texto = new StringBuilder();
        foreach (ParteDoChat parte in Partes(r, campo))
            texto.Append(parte.Texto);
        return texto.ToString();
    }

    private static string? Texto(JsonElement e, string campo) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static IEnumerable<JsonElement> Lista(JsonElement e, string campo) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];

    private static JsonElement? Caminho(JsonElement e, params string[] campos)
    {
        foreach (string campo in campos)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(campo, out e))
                return null;
        }
        return e;
    }
}
