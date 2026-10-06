using System.Text.RegularExpressions;

namespace OlhoNoChat.Kick;

/// <summary>
/// O canal da Kick digitado na faixa do canal: "nome", "@nome" ou o link do canal ("kick.com/nome", "https://kick.com/nome/videos").
/// Gravado nas opções como o nome do endereço, em minúsculas e com "-" no lugar de "_" (o nome "Fulano_1" tem o endereço
/// kick.com/fulano-1): é o que a página do chat procura na Kick (browser/chat.js).
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
        return Nome().IsMatch(nome) ? nome.ToLowerInvariant().Replace('_', '-') : null;
    }
}
