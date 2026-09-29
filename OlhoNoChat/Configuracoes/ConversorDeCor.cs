using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace OlhoNoChat.Configuracoes;

/// <summary>Cores no arquivo como "#AARRGGBB" (o mesmo texto do arquivo antigo).</summary>
public sealed class ConversorDeCor : JsonConverter<Color>
{
    /// <summary>"#96F5F500", "#F5F500" ou um nome de cor do WPF; null quando não é uma cor.</summary>
    public static Color? Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        try
        {
            return ColorConverter.ConvertFromString(texto.Trim()) as Color?;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Ler(reader.TokenType == JsonTokenType.String ? reader.GetString() : null)
        ?? throw new JsonException("Cor inválida no arquivo de configurações.");

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
