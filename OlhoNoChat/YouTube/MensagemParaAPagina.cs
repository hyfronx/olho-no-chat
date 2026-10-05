using System.Text;
using System.Text.RegularExpressions;
using YTLiveChat.Contracts.Models;

namespace OlhoNoChat.YouTube;

/// <summary>
/// Uma mensagem do chat do YouTube no formato que a página do Padrão entende (<c>oncChat.addYouTube</c>, ver
/// browser/chat.js). <see cref="Partes"/> tem textos e emojis próprios do canal como
/// <c>{ emote: { src }, name }</c>, igual aos emotes das mensagens da Twitch.
/// </summary>
public sealed partial record MensagemParaAPagina(
    string Id,
    string Login,
    string Nome,
    string Papel,
    IReadOnlyList<object> Partes,
    SuperChatParaAPagina? SuperChat,
    string? Aviso)
{
    /// <summary>O dono do canal, um moderador, um membro, ou "" para os outros.</summary>
    public const string Dono = "owner", Moderador = "moderator", Membro = "member";

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex CorHex();

    /// <summary>A mensagem que a biblioteca leu, para a página; null quando não há nada para mostrar.</summary>
    public static MensagemParaAPagina? De(ChatItem item)
    {
        string nome = (item.Author.Name ?? string.Empty).Trim();
        // O YouTube mostra o @ como nome; sem @, o nome mesmo (os filtros comparam em minúsculas, sem o @)
        string login = (item.Author.ChannelHandle is { Length: > 0 } arroba ? arroba : nome).TrimStart('@').ToLowerInvariant();
        if (nome.Length == 0)
            nome = login;

        string papel = item.IsOwner ? Dono : item.IsModerator ? Moderador : item.IsMembership ? Membro : string.Empty;
        List<object> partes = PartesDe(item.Message);

        SuperChatParaAPagina? superChat = item.Superchat is { } sc
            ? new SuperChatParaAPagina(sc.AmountString, CorHex().IsMatch(sc.BodyBackgroundColor ?? string.Empty) ? "#" + sc.BodyBackgroundColor : "#1E88E5")
            : null;
        string? aviso = item.MembershipDetails is { } membro ? AvisoDeMembro(membro) : null;

        if (partes.Count == 0 && superChat == null && aviso == null)
            return null;
        return new MensagemParaAPagina(item.Id, login, nome, papel, partes, superChat, aviso);
    }

    // Textos juntos num pedaço só; emojis comuns viram o próprio caractere (como na Twitch); os do canal, imagem
    private static List<object> PartesDe(MessagePart[]? mensagem)
    {
        var partes = new List<object>();
        var texto = new StringBuilder();
        foreach (MessagePart parte in mensagem ?? [])
        {
            switch (parte)
            {
                case TextPart t:
                    texto.Append(t.Text);
                    break;
                case EmojiPart e when !e.IsCustomEmoji && !string.IsNullOrEmpty(e.EmojiText):
                    texto.Append(e.EmojiText);
                    break;
                case ImagePart imagem when !string.IsNullOrEmpty(imagem.Url):
                    if (texto.Length > 0)
                    {
                        partes.Add(texto.ToString());
                        texto.Clear();
                    }
                    string nome = imagem is EmojiPart emoji && !string.IsNullOrEmpty(emoji.EmojiText) ? emoji.EmojiText : imagem.Alt ?? string.Empty;
                    partes.Add(new { emote = new { src = imagem.Url }, name = nome });
                    break;
            }
        }
        if (texto.Length > 0)
            partes.Add(texto.ToString());
        return partes;
    }

    private static string AvisoDeMembro(MembershipDetails membro) => membro.EventType switch
    {
        MembershipEventType.New => "virou membro do canal!",
        MembershipEventType.Upgraded => "subiu de nível como membro!",
        MembershipEventType.Milestone => membro.MilestoneMonths is int meses and > 0
            ? $"é membro há {meses} {(meses == 1 ? "mês" : "meses")}!"
            : "comemora mais um tempo como membro!",
        MembershipEventType.GiftPurchase => membro.GiftCount is int quantas and > 1
            ? $"deu {quantas} assinaturas de membro!"
            : "deu uma assinatura de membro!",
        MembershipEventType.GiftRedemption => string.IsNullOrWhiteSpace(membro.GifterUsername)
            ? "ganhou uma assinatura de membro!"
            : $"ganhou uma assinatura de membro de {membro.GifterUsername.Trim()}!",
        _ => "é membro do canal!",
    };
}

/// <summary>Um Super Chat: o valor como o YouTube escreve ("R$ 10,00") e a cor dele.</summary>
public sealed record SuperChatParaAPagina(string Valor, string Cor);
