namespace OlhoNoChat.Janelas.Chat;

/// <summary>Um grupo da lista de emotes: o título laranja ("{canal} (este canal)", outro canal, "Globais da Twitch") e os emotes.</summary>
public sealed record GrupoDeEmotes(string Titulo, IReadOnlyList<EmoteNaLista> Emotes)
{
    /// <summary>Os grupos só com os emotes que têm o texto no nome (sem diferenciar maiúsculas); grupos vazios somem.</summary>
    public static IReadOnlyList<GrupoDeEmotes> Filtrar(IReadOnlyList<GrupoDeEmotes> grupos, string busca)
    {
        busca = busca.Trim();
        if (busca.Length == 0)
            return grupos;
        return grupos
            .Select(g => new GrupoDeEmotes(g.Titulo, g.Emotes.Where(e => e.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(g => g.Emotes.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Os grupos em linhas para a lista (que só desenha as linhas que aparecem): o título de cada grupo e os emotes dele,
    /// <paramref name="colunas"/> por linha.
    /// </summary>
    public static IReadOnlyList<object> EmLinhas(IReadOnlyList<GrupoDeEmotes> grupos, int colunas)
    {
        colunas = Math.Max(1, colunas);
        var linhas = new List<object>();
        foreach (GrupoDeEmotes grupo in grupos)
        {
            linhas.Add(new TituloDoGrupo(grupo.Titulo, Primeiro: linhas.Count == 0));
            linhas.AddRange(grupo.Emotes.Chunk(colunas).Select(emotes => new LinhaDeEmotes(emotes)));
        }
        return linhas;
    }
}

/// <summary>Uma linha da lista de emotes com o título de um grupo (o primeiro fica mais perto do topo).</summary>
public sealed record TituloDoGrupo(string Titulo, bool Primeiro);

/// <summary>Uma linha da lista de emotes com os emotes lado a lado.</summary>
public sealed record LinhaDeEmotes(IReadOnlyList<EmoteNaLista> Emotes);
