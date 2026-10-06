namespace OlhoNoChat.YouTube;

/// <summary>Quem mandou a mensagem, pelos selos do YouTube (do mais importante para o menos).</summary>
public enum PapelNoChat
{
    Nenhum,
    Membro,
    Moderador,
    Dono,
}

/// <summary>Os avisos de membros do canal.</summary>
public enum TipoDeEventoDeMembro
{
    /// <summary>Virou membro.</summary>
    Novo,

    /// <summary>Subiu de nível.</summary>
    Subiu,

    /// <summary>Comemora um tempo como membro (com a mensagem dele).</summary>
    Marco,

    /// <summary>Deu assinaturas de presente.</summary>
    DeuPresente,

    /// <summary>Ganhou uma assinatura de presente.</summary>
    GanhouPresente,
}

/// <summary>
/// Uma mensagem do chat de uma live do YouTube, como o <see cref="RespostaDoChat"/> leu: textos, Super Chat ou aviso de
/// membro. <see cref="MensagemParaAPagina"/> a passa para o formato da página.
/// </summary>
/// <param name="Nome">O nome que o YouTube mostra (hoje o @ do canal).</param>
/// <param name="Partes">O texto em pedaços; um emoji próprio do canal tem <see cref="ParteDoChat.Imagem"/>.</param>
public sealed record ItemDoChat(
    string Id,
    DateTimeOffset EnviadaEm,
    string Nome,
    PapelNoChat Papel,
    IReadOnlyList<ParteDoChat> Partes,
    SuperChatDoChat? SuperChat = null,
    EventoDeMembro? Membro = null);

/// <summary>Um pedaço da mensagem: texto (emojis comuns já são o próprio caractere) ou emoji do canal (":nome:" e a imagem).</summary>
public sealed record ParteDoChat(string Texto, string? Imagem = null);

/// <summary>Um Super Chat (ou Super Sticker): o valor como o YouTube escreve ("R$ 10,00") e a cor de fundo em RRGGBB.</summary>
public sealed record SuperChatDoChat(string Valor, string? Cor);

/// <summary>Um aviso de membro: os meses do marco, quantas assinaturas deu ou quem deu a que ganhou.</summary>
public sealed record EventoDeMembro(TipoDeEventoDeMembro Tipo, int? Meses = null, int? Presentes = null, string? QuemDeu = null);
