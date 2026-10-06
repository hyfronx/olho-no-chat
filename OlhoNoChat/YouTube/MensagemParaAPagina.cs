using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>A mensagem que o leitor leu, para a página; null quando não há nada para mostrar.</summary>
    public static MensagemParaAPagina? De(ItemDoChat item)
    {
        string nome = item.Nome.Trim();
        // O YouTube mostra o @ como nome (os filtros comparam em minúsculas, sem o @)
        string login = nome.TrimStart('@').ToLowerInvariant();
        if (nome.Length == 0)
            nome = login;

        string papel = item.Papel switch
        {
            PapelNoChat.Dono => Dono,
            PapelNoChat.Moderador => Moderador,
            PapelNoChat.Membro => Membro,
            _ => string.Empty,
        };
        List<object> partes = PartesDe(item.Partes);

        SuperChatParaAPagina? superChat = item.SuperChat is { } sc
            ? new SuperChatParaAPagina(sc.Valor, CorHex().IsMatch(sc.Cor ?? string.Empty) ? "#" + sc.Cor : "#1E88E5")
            : null;
        string? aviso = item.Membro is { } membro ? AvisoDeMembro(membro) : null;

        if (partes.Count == 0 && superChat == null && aviso == null)
            return null;
        return new MensagemParaAPagina(item.Id, login, nome, papel, partes, superChat, aviso);
    }

    // Textos juntos num pedaço só (os emojis comuns já são o caractere, como na Twitch); os do canal, imagem
    private static List<object> PartesDe(IReadOnlyList<ParteDoChat> mensagem)
    {
        var partes = new List<object>();
        var texto = new StringBuilder();
        foreach (ParteDoChat parte in mensagem)
        {
            if (parte.Imagem == null)
            {
                texto.Append(parte.Texto);
                continue;
            }
            if (texto.Length > 0)
            {
                partes.Add(texto.ToString());
                texto.Clear();
            }
            partes.Add(new { emote = new { src = parte.Imagem }, name = parte.Texto });
        }
        if (texto.Length > 0)
            partes.Add(texto.ToString());
        return partes;
    }

    private static string AvisoDeMembro(EventoDeMembro membro) => membro.Tipo switch
    {
        TipoDeEventoDeMembro.Novo => "virou membro do canal!",
        TipoDeEventoDeMembro.Subiu => "subiu de nível como membro!",
        TipoDeEventoDeMembro.Marco => membro.Meses is int meses and > 0
            ? $"é membro há {meses} {(meses == 1 ? "mês" : "meses")}!"
            : "comemora mais um tempo como membro!",
        TipoDeEventoDeMembro.DeuPresente => membro.Presentes is int quantas and > 1
            ? $"deu {quantas} assinaturas de membro!"
            : "deu uma assinatura de membro!",
        _ => string.IsNullOrWhiteSpace(membro.QuemDeu) // GanhouPresente
            ? "ganhou uma assinatura de membro!"
            : $"ganhou uma assinatura de membro de {membro.QuemDeu.Trim()}!",
    };
}

/// <summary>Um Super Chat: o valor como o YouTube escreve ("R$ 10,00") e a cor dele.</summary>
public sealed record SuperChatParaAPagina(string Valor, string Cor);
