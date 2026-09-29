using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OlhoNoChat.Configuracoes;

/// <summary>
/// O token da Twitch no arquivo: protegido pelo Windows (DPAPI), só o mesmo usuário do Windows neste computador
/// consegue abrir. Quem copia a pasta para outro computador precisa conectar de novo.
/// </summary>
public sealed class ProtecaoDoToken : JsonConverter<string>
{
    // Separa o que o Olho no Chat protege do que outros programas do mesmo usuário protegem
    private static readonly byte[] Complemento = Encoding.UTF8.GetBytes("OlhoNoChat.TokenDaTwitch");

    public static string Proteger(string token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;
        byte[] protegido = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Complemento, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protegido);
    }

    /// <summary>O token, ou "" quando não abre (outro computador, outro usuário, texto estragado).</summary>
    public static string Abrir(string? protegido)
    {
        if (string.IsNullOrEmpty(protegido))
            return string.Empty;
        try
        {
            byte[] aberto = ProtectedData.Unprotect(Convert.FromBase64String(protegido), Complemento, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(aberto);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            Debug.WriteLine($"O token gravado não abre neste computador: {ex.Message}");
            return string.Empty;
        }
    }

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Abrir(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Proteger(value));
}
