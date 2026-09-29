using System.Text.RegularExpressions;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Nomes de canal e de usuário digitados: "nome", "@nome", "twitch.tv/nome" ou um link copiado do navegador
/// ("https://www.twitch.tv/nome/videos", "twitch.tv/popout/nome/chat?popout=").
/// </summary>
public static partial class NomesDaTwitch
{
    public const string DicaNomeInvalido = "Use o nome como aparece no endereço do canal (twitch.tv/nome): só letras, números e _.";

    // Endereços em que o canal vem depois de mais uma parte (twitch.tv/popout/nome/chat)
    private static readonly string[] PaginasAntesDoCanal = ["popout", "moderator", "embed"];

    [GeneratedRegex("^[A-Za-z0-9_]{1,25}$")]
    private static partial Regex Login();

    /// <summary>O nome dentro do que foi digitado ("" se nada). Pode ainda ser inválido: ver <see cref="EhValido"/>.</summary>
    public static string Extrair(string? texto)
    {
        string nome = (texto ?? string.Empty).Trim();

        int site = nome.IndexOf("twitch.tv", StringComparison.OrdinalIgnoreCase);
        if (site >= 0)
        {
            string caminho = nome[(site + "twitch.tv".Length)..].Split('?', '#')[0];
            string[] partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int indice = partes.Length > 1 && PaginasAntesDoCanal.Contains(partes[0], StringComparer.OrdinalIgnoreCase) ? 1 : 0;
            nome = partes.Length > indice ? partes[indice] : string.Empty;
        }
        else
        {
            nome = nome.TrimEnd('/');
            int barra = nome.LastIndexOf('/');
            if (barra >= 0)
                nome = nome[(barra + 1)..];
        }

        return nome.Trim().TrimStart('@');
    }

    public static bool EhValido(string? nome) => nome != null && Login().IsMatch(nome);
}
