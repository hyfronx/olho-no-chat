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
}
