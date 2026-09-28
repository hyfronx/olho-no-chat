#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Newtonsoft.Json;

namespace OlhoNoChat.Atalhos;

/// <summary>
/// Uma combinação de teclas, como Ctrl + Alt + F9. Sem atalho = null (ou tecla <see cref="Key.None"/>, em arquivos
/// antigos).
/// </summary>
/// <remarks>
/// No arquivo de configurações o atalho é gravado como {"Key": n, "Modifiers": m}, com os números do WPF. Os
/// nomes do JSON ficam em inglês até a etapa 3 da reescrita trocar o arquivo; as propriedades têm set porque o
/// leitor de hoje preenche o objeto que já existe.
/// </remarks>
public sealed record Atalho
{
    [JsonProperty("Key")]
    public Key Tecla { get; set; }

    [JsonProperty("Modifiers")]
    public ModifierKeys Modificadores { get; set; }

    [JsonConstructor]
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
}
