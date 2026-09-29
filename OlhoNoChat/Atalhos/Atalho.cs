using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace OlhoNoChat.Atalhos;

/// <summary>
/// Uma combinação de teclas, como Ctrl + Alt + F9. Sem atalho = null (ou tecla <see cref="Key.None"/>, em arquivos
/// antigos).
/// </summary>
/// <remarks>
/// No arquivo de configurações fica {"Tecla": n, "Modificadores": m}, com os números do WPF (o arquivo das
/// versões até a 1.5 usava {"Key": n, "Modifiers": m}: ver <see cref="Configuracoes.ImportacaoDoArquivoAntigo"/>).
/// </remarks>
public sealed record Atalho
{
    public Key Tecla { get; init; }

    public ModifierKeys Modificadores { get; init; }

    public Atalho() { }

    public Atalho(Key tecla, ModifierKeys modificadores)
    {
        Tecla = tecla;
        Modificadores = modificadores;
    }

    /// <summary>Há um atalho de verdade (não é vazio nem sem tecla).</summary>
    public static bool Existe([NotNullWhen(true)] Atalho? atalho) => atalho is { Tecla: not Key.None };

    /// <summary>
    /// Como o atalho aparece para a pessoa: os modificadores na ordem Ctrl, Shift, Alt, Win e depois a tecla, com o
    /// nome que o WPF dá a ela ("F9", "A", "D1"...).
    /// </summary>
    public override string ToString()
    {
        var partes = new List<string>(5);
        if (Modificadores.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (Modificadores.HasFlag(ModifierKeys.Shift)) partes.Add("Shift");
        if (Modificadores.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        if (Modificadores.HasFlag(ModifierKeys.Windows)) partes.Add("Win");
        partes.Add(Tecla.ToString());
        return string.Join(" + ", partes);
    }

    /// <summary>Texto do atalho, ou "Nenhum".</summary>
    public static string TextoOuNenhum(Atalho? atalho) => Existe(atalho) ? atalho.ToString() : "Nenhum";

    /// <summary>Uma dica com "Atalho: ..." na linha de baixo; sem atalho, só a dica.</summary>
    public static string NaDica(string dica, Atalho? atalho) => Existe(atalho) ? $"{dica}\nAtalho: {atalho}" : dica;
}
