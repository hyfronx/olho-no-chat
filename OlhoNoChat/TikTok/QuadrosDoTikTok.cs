using System.IO;
using System.IO.Compression;

namespace OlhoNoChat.TikTok;

/// <summary>Quem mandou a mensagem no TikTok (do mais importante para o menos).</summary>
public enum PapelNoTikTok
{
    Nenhum,
    Assinante,
    Moderador,
    Dono,
}

/// <summary>
/// Uma mensagem do chat de uma live do TikTok: texto e emotes da live (a imagem) na ordem em que aparecem.
/// <see cref="Login"/> é o @ (sem o @, em minúsculas), <see cref="Nome"/> o apelido que o TikTok mostra.
/// </summary>
public sealed record ItemDoTikTok(string Id, string IdDoUsuario, string Login, string Nome, PapelNoTikTok Papel, IReadOnlyList<ParteDoTikTok> Partes);

/// <summary>Um pedaço da mensagem: texto, ou um emote da live (<see cref="Imagem"/>).</summary>
public sealed record ParteDoTikTok(string Texto, string? Imagem = null);

/// <summary>
/// O que veio num quadro do WebSocket: as mensagens novas, as que um moderador apagou (pelo id da mensagem ou de
/// quem mandou), se a live acabou, o id do quadro a confirmar (<see cref="QuadrosDoTikTok.Confirmar"/>, ou 0), as
/// mensagens de presente (ainda por juntar, ver <see cref="JuntadorDePresentes"/>) e os outros avisos (seguiu,
/// compartilhou, Superfã) e quantos assistem agora (WebcastRoomUserSeqMessage; null quando não veio).
/// </summary>
public sealed record LoteDoTikTok(
    IReadOnlyList<ItemDoTikTok> Mensagens,
    IReadOnlyList<string> Apagadas,
    IReadOnlyList<string> UsuariosApagados,
    bool LiveAcabou,
    ulong Confirmar,
    IReadOnlyList<EnvioDePresente> Presentes,
    IReadOnlyList<AvisoDoTikTok> Avisos,
    long? Viewers = null)
{
    public static readonly LoteDoTikTok Vazio = new([], [], [], false, 0, [], []);
}

/// <summary>
/// Os quadros do WebSocket do chat do TikTok ("webcast"), em protobuf, como o site manda e recebe: entrar na sala como
/// espectador, o "estou aqui" de tempos em tempos, a confirmação de cada lote, e a leitura dos lotes de mensagens
/// (em gzip). Só os campos que o app usa; os números dos campos vêm dos esquemas do TikTok (os mesmos que os
/// projetos da comunidade usam).
/// </summary>
public static class QuadrosDoTikTok
{
    /// <summary>Entra na sala da live como espectador (o primeiro quadro depois de conectar).</summary>
    public static byte[] EntrarNaSala(string idDaSala)
    {
        byte[] entrar = new ProtobufNovo()
            .Numero(1, ulong.Parse(idDaSala))
            .Numero(4, 12) // live_id: sempre 12 no TikTok LIVE
            .Texto(5, "audience")
            .ParaBytes();
        return Quadro("im_enter_room", entrar, 0);
    }

    /// <summary>O "estou aqui" (sem ele o TikTok fecha a conexão).</summary>
    public static byte[] Batida(string idDaSala) =>
        Quadro("hb", new ProtobufNovo().Numero(1, ulong.Parse(idDaSala)).ParaBytes(), 0);

    /// <summary>Confirma um lote recebido (o site manda "-").</summary>
    public static byte[] Confirmar(ulong idDoQuadro) => Quadro("ack", "-"u8, idDoQuadro);

    private static byte[] Quadro(string tipo, ReadOnlySpan<byte> dados, ulong idDoQuadro)
    {
        var quadro = new ProtobufNovo();
        if (idDoQuadro != 0)
            quadro.Numero(2, idDoQuadro);
        return quadro.Texto(6, "pb").Texto(7, tipo).Bytes(8, dados).ParaBytes();
    }

    /// <summary>
    /// Lê um quadro recebido. Só os do tipo "msg" têm mensagens; os outros (resposta da entrada, batidas) vêm vazios.
    /// O histórico que o TikTok manda ao entrar só vem com <paramref name="comHistorico"/>.
    /// </summary>
    /// <param name="idDoDono">O id do dono da live (as mensagens dele saem como <see cref="PapelNoTikTok.Dono"/>).</param>
    /// <exception cref="InvalidDataException">O quadro não é o esperado.</exception>
    public static LoteDoTikTok Ler(ReadOnlyMemory<byte> quadro, string idDoDono, bool comHistorico)
    {
        var envelope = Protobuf.Ler(quadro);
        if (envelope.Texto(7) != "msg" || envelope.Bytes(8) is not { Length: > 0 } dados)
            return LoteDoTikTok.Vazio;

        var lote = Protobuf.Ler(Descompactar(dados));
        var mensagens = new List<ItemDoTikTok>();
        var apagadas = new List<string>();
        var usuariosApagados = new List<string>();
        var presentes = new List<EnvioDePresente>();
        var avisos = new List<AvisoDoTikTok>();
        bool acabou = false;
        long? viewers = null;
        foreach (Protobuf bruta in lote.Mensagens(1))
        {
            if (bruta.Sim(6) && !comHistorico) // histórico
                continue;
            if (bruta.Bytes(2) is not { } corpo)
                continue;
            string tipo = bruta.Texto(1);
            var m = Protobuf.Ler(corpo);
            switch (tipo)
            {
                case "WebcastChatMessage":
                    if (Mensagem(m, IdDe(m, bruta), m.Mensagem(2), m.Mensagem(18), idDoDono, Partes(m.Texto(3), m.Mensagens(13))) is { } chat)
                        mensagens.Add(chat);
                    break;
                case "WebcastEmoteChatMessage":
                    var emotes = m.Mensagens(3).Select(Emote).OfType<ParteDoTikTok>().ToList();
                    if (Mensagem(m, IdDe(m, bruta), m.Mensagem(2), m.Mensagem(5), idDoDono, emotes) is { } soEmotes)
                        mensagens.Add(soEmotes);
                    break;
                case "WebcastImDeleteMessage":
                    apagadas.AddRange(m.Numeros(2).Select(id => id.ToString()));
                    usuariosApagados.AddRange(m.Numeros(3).Select(id => id.ToString()));
                    break;
                case "WebcastControlMessage":
                    // 3 = a live acabou, 4 = o TikTok suspendeu a live
                    acabou |= m.Numero(2) is 3 or 4;
                    break;
                case "WebcastGiftMessage":
                    if (Presente(m, IdDe(m, bruta)) is { } presente)
                        presentes.Add(presente);
                    break;
                case "WebcastSocialMessage":
                    // Campo 4: 1 = seguiu, 3 = compartilhou (4 = repostou: fica de fora)
                    TipoDeAvisoDoTikTok? social = m.Numero(4) switch
                    {
                        1 => TipoDeAvisoDoTikTok.Seguiu,
                        3 => TipoDeAvisoDoTikTok.Compartilhou,
                        _ => null,
                    };
                    if (social is { } tipoSocial && Aviso(IdDe(m, bruta), m.Mensagem(2), tipoSocial) is { } aviso)
                        avisos.Add(aviso);
                    break;
                case "WebcastBarrageMessage":
                    // A faixa de destaque; a de quem virou Superfã tem a chave "ttlive_superFan_..._someoneBecameSuperFan"
                    // (a de "Superfã entrou" não conta)
                    string chave = m.Mensagem(5)?.Texto(1) is { Length: > 0 } k ? k : m.Mensagem(24)?.Texto(1) ?? string.Empty;
                    if (chave.Contains("superFan", StringComparison.OrdinalIgnoreCase) && chave.Contains("Became", StringComparison.OrdinalIgnoreCase)
                        && Aviso(IdDe(m, bruta), m.Mensagem(50), TipoDeAvisoDoTikTok.Superfa) is { } superfa)
                        avisos.Add(superfa);
                    break;
                case "WebcastRoomUserSeqMessage":
                    // Campo 3: quantos estão na live agora (o 7 é o total de quem já passou)
                    viewers = (long)m.Numero(3);
                    break;
                case "WebcastSubNotifyMessage":
                    // O aviso antigo de assinatura: campo 7 = 0 é "assinou"
                    if (m.Numero(7) == 0 && Aviso(IdDe(m, bruta), m.Mensagem(2), TipoDeAvisoDoTikTok.Superfa) is { } assinou)
                        avisos.Add(assinou);
                    break;
            }
        }
        return new LoteDoTikTok(mensagens, apagadas, usuariosApagados, acabou, envelope.Numero(2), presentes, avisos, viewers);
    }

    // Uma mensagem de presente: quem (7), qual (2; 15 = 16 nome, 1.1 imagem, 12 moedas, 11 tipo), contagem (5), fim (9) e
    // o id do combo (11)
    private static EnvioDePresente? Presente(Protobuf m, string id)
    {
        if (Pessoa(m.Mensagem(7)) is not { } pessoa || m.Mensagem(15) is not { } presente)
            return null;
        long idDoPresente = (long)m.Numero(2);
        if (idDoPresente == 0)
            idDoPresente = (long)presente.Numero(5);
        string? imagem = presente.Mensagem(1)?.Texto(1);
        return new EnvioDePresente(id, pessoa.Id, pessoa.Login, pessoa.Nome, idDoPresente, presente.Texto(16).Trim(),
            string.IsNullOrEmpty(imagem) || !imagem.StartsWith("https://", StringComparison.Ordinal) ? null : imagem,
            (int)presente.Numero(12), (int)presente.Numero(11), m.Numero(11), (int)m.Numero(5), m.Sim(9));
    }

    private static AvisoDoTikTok? Aviso(string id, Protobuf? usuario, TipoDeAvisoDoTikTok tipo) =>
        Pessoa(usuario) is { } pessoa ? new AvisoDoTikTok(id, pessoa.Id, pessoa.Login, pessoa.Nome, tipo) : null;

    // Quem: o id (1), o @ (38) e o apelido (3; sem ele, o @)
    private static (string Id, string Login, string Nome)? Pessoa(Protobuf? usuario)
    {
        if (usuario == null)
            return null;
        string login = usuario.Texto(38).Trim().ToLowerInvariant();
        string nome = usuario.Texto(3).Trim();
        if (nome.Length == 0)
            nome = login;
        return nome.Length == 0 ? null : (usuario.Numero(1).ToString(), login, nome);
    }

    private static ReadOnlyMemory<byte> Descompactar(ReadOnlyMemory<byte> dados)
    {
        ReadOnlySpan<byte> b = dados.Span;
        if (b.Length < 3 || b[0] != 0x1F || b[1] != 0x8B || b[2] != 0x08)
            return dados;
        using var gzip = new GZipStream(new MemoryStream(dados.ToArray()), CompressionMode.Decompress);
        using var saida = new MemoryStream();
        gzip.CopyTo(saida);
        return saida.ToArray();
    }

    // O id fica no cabeçalho da mensagem (campo 1); quando falta, o do envelope
    private static string IdDe(Protobuf m, Protobuf bruta)
    {
        ulong id = m.Mensagem(1)?.Numero(2) ?? 0;
        return (id != 0 ? id : bruta.Numero(3)).ToString();
    }

    private static ItemDoTikTok? Mensagem(Protobuf m, string id, Protobuf? usuario, Protobuf? identidade, string idDoDono,
        IReadOnlyList<ParteDoTikTok> partes)
    {
        if (partes.Count == 0 || Pessoa(usuario) is not var (idDoUsuario, login, nome))
            return null;

        PapelNoTikTok papel = idDoUsuario == idDoDono && idDoDono.Length > 0 ? PapelNoTikTok.Dono
            : identidade?.Sim(5) == true ? PapelNoTikTok.Moderador
            : identidade?.Sim(2) == true ? PapelNoTikTok.Assinante
            : PapelNoTikTok.Nenhum;
        return new ItemDoTikTok(id, idDoUsuario, login, nome, papel, partes);
    }

    // O texto com os emotes da live no lugar deles: cada emote diz a posição (em caracteres do texto) onde entra
    private static List<ParteDoTikTok> Partes(string texto, IEnumerable<Protobuf> emotesComPosicao)
    {
        var emotes = emotesComPosicao
            .Select(e => (Posicao: (long)e.Numero(1), Parte: Emote(e.Mensagem(2))))
            .Where(e => e.Parte != null)
            .OrderBy(e => e.Posicao)
            .ToList();

        var partes = new List<ParteDoTikTok>();
        int[] letras = Letras(texto);
        int inicio = 0;
        foreach (var (posicao, emote) in emotes)
        {
            int ate = (int)Math.Clamp(posicao, inicio, letras.Length - 1);
            if (letras[ate] > letras[inicio])
                partes.Add(new ParteDoTikTok(texto[letras[inicio]..letras[ate]]));
            partes.Add(emote!);
            inicio = ate;
        }
        if (letras[^1] > letras[inicio])
            partes.Add(new ParteDoTikTok(texto[letras[inicio]..]));
        return partes;
    }

    // Onde cada caractere começa no texto, mais o fim: um emoji conta como um caractere (não dois do UTF-16)
    private static int[] Letras(string texto)
    {
        var inicios = new List<int>();
        for (int i = 0; i < texto.Length; i += char.IsSurrogatePair(texto, i) ? 2 : 1)
            inicios.Add(i);
        inicios.Add(texto.Length);
        return [.. inicios];
    }

    // Um emote da live: o id (1) e a imagem (2, com a lista de endereços no campo 1)
    private static ParteDoTikTok? Emote(Protobuf? emote)
    {
        string? imagem = emote?.Mensagem(2)?.Texto(1);
        return string.IsNullOrEmpty(imagem) || !imagem.StartsWith("https://", StringComparison.Ordinal) ? null : new ParteDoTikTok(string.Empty, imagem);
    }
}
