using System.Globalization;
using System.Windows.Media;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>
/// As cores prontas das listas de cores dos filtros e a leitura da cor "Personalizada…" digitada. Todas ficam
/// translúcidas no chat (transparência 150 de 255), como as prontas.
/// </summary>
public static class CoresDeDestaque
{
    public const byte Transparencia = 150;
    public const string NomeDaPersonalizada = "Personalizada…";
    public const string ErroDaPersonalizada = "Use o formato #RRGGBB.";

    public static IReadOnlyList<(string Nome, Color Cor)> Prontas { get; } =
    [
        ("Amarelo", Translucida(0xF5, 0xF5, 0x00)),
        ("Laranja", Translucida(0xFF, 0x8A, 0x00)),
        ("Vermelho", Translucida(0xE5, 0x39, 0x35)),
        ("Rosa", Translucida(0xDB, 0x33, 0xB3)),
        ("Roxo", Translucida(0x8A, 0x2B, 0xE2)),
        ("Azul", Translucida(0x1E, 0x90, 0xFF)),
        ("Azul-claro", Translucida(0x00, 0xBC, 0xD4)),
        ("Verde", Translucida(0x00, 0xAD, 0x03)),
        ("Branco", Translucida(0xFF, 0xFF, 0xFF)),
        ("Cinza", Translucida(0x80, 0x80, 0x80)),
    ];

    private static Color Translucida(byte r, byte g, byte b) => Color.FromArgb(Transparencia, r, g, b);

    /// <summary>
    /// A cor de "#RRGGBB" ou "RRGGBB" (null se não for uma cor). A cor já salva digitada de novo mantém a
    /// transparência que tinha.
    /// </summary>
    public static Color? Ler(string? texto, Color salva)
    {
        string hex = (texto ?? string.Empty).Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
            return null;

        var cor = Translucida((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return cor.R == salva.R && cor.G == salva.G && cor.B == salva.B ? salva : cor;
    }

    /// <summary>"#RRGGBB" de uma cor (sem a transparência), para mostrar na caixa.</summary>
    public static string ParaTexto(Color cor) => $"#{cor.R:X2}{cor.G:X2}{cor.B:X2}";
}
