using System.IO;

namespace OlhoNoChat.Som;

/// <summary>A pasta de sons (a do app ou uma escolhida na aba Som) e a lista com os nomes mostrados.</summary>
public static class SonsDisponiveis
{
    /// <summary>O som "Nenhum", como fica gravado.</summary>
    public const string Nenhum = "None";

    /// <summary>A pasta dos sons que vêm com o app, como fica gravada.</summary>
    public const string PastaPadrao = "Default";

    /// <param name="Arquivo">Nome do arquivo, como fica gravado ("job-done.wav").</param>
    /// <param name="Nome">Como aparece na lista ("Job done").</param>
    public sealed record Som(string Arquivo, string Nome);

    public static string PastaDosSonsDoApp => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>A pasta de verdade: a do app para "Default", vazio ou uma pasta escolhida que não existe mais.</summary>
    public static string ResolverPasta(string? escolhida) =>
        string.IsNullOrWhiteSpace(escolhida) || escolhida == PastaPadrao || !Directory.Exists(escolhida)
            ? PastaDosSonsDoApp
            : escolhida;

    /// <summary>Os .wav e depois os .mp3 da pasta (na ordem em que o Windows lista cada tipo).</summary>
    public static IReadOnlyList<Som> Listar(string pasta)
    {
        if (!Directory.Exists(pasta))
            return [];
        return Directory.GetFiles(pasta, "*.wav").Concat(Directory.GetFiles(pasta, "*.mp3"))
            .Select(Path.GetFileName)
            .OfType<string>()
            // "*.wav" do Windows também pega ".wavx": só as extensões certas
            .Where(arquivo => Path.GetExtension(arquivo).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                              || Path.GetExtension(arquivo).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            .Select(arquivo => new Som(arquivo, NomeMostrado(arquivo)))
            .ToList();
    }

    /// <summary>"job-done.wav" → "Job done".</summary>
    public static string NomeMostrado(string arquivo)
    {
        string nome = Path.GetFileNameWithoutExtension(arquivo).Replace('-', ' ').Replace('_', ' ');
        return nome.Length == 0 ? nome : char.ToUpper(nome[0]) + nome[1..];
    }

    /// <summary>O caminho do som escolhido; null com "Nenhum" ou quando o arquivo não existe.</summary>
    public static string? Caminho(string? pastaEscolhida, string? som)
    {
        if (string.IsNullOrWhiteSpace(som) || som.Equals(Nenhum, StringComparison.OrdinalIgnoreCase))
            return null;
        string caminho = Path.Combine(ResolverPasta(pastaEscolhida), som);
        return File.Exists(caminho) ? caminho : null;
    }
}
