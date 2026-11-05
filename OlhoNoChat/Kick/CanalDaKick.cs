using System.Text.RegularExpressions;

namespace OlhoNoChat.Kick;

/// <summary>
/// O canal da Kick digitado na faixa do canal: "nome", "@nome" ou o link do canal ("kick.com/nome", "https://kick.com/nome/videos").
/// Gravado nas opções em minúsculas, como foi digitado: é o que o leitor procura na Kick (<see cref="LeitorDaKick"/>).
/// O "_" fica: quase sempre o endereço troca o "_" do nome por "-" ("Fulano_1" tem o endereço kick.com/fulano-1), mas há
/// canais em que o "_" ficou no endereço (kick.com/korekore_ch); o leitor tenta a outra forma (<see cref="OutraForma"/>).
/// </summary>
public static partial class CanalDaKick
{
    public const string DicaInvalido = "Use o nome como aparece no endereço do canal (kick.com/nome): só letras, números, _ e -.";

    [GeneratedRegex("^[A-Za-z0-9_-]{2,30}$")]
    private static partial Regex Nome();

    /// <summary>O nome do canal no que foi digitado (ou gravado); null se vazio ou se não dá para entender.</summary>
    public static string? Ler(string? digitado)
    {
        string texto = (digitado ?? string.Empty).Trim();

        int site = texto.IndexOf("kick.com", StringComparison.OrdinalIgnoreCase);
        if (site >= 0)
        {
            string caminho = texto[(site + "kick.com".Length)..].Split('?', '#')[0];
            texto = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }

        string nome = texto.TrimStart('@');
        return Nome().IsMatch(nome) ? nome.ToLowerInvariant() : null;
    }

    /// <summary>
    /// O mesmo canal com "_" no lugar de "-", ou o contrário (a Kick não achou o nome como está), ou null se não tem nenhum
    /// dos dois. Com os dois, troca o "_" (o mais comum no endereço é o "-").
    /// </summary>
    public static string? OutraForma(string canal) =>
        canal.Contains('_') ? canal.Replace('_', '-')
        : canal.Contains('-') ? canal.Replace('-', '_')
        : null;
}
