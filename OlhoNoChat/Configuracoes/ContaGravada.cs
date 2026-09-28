#nullable enable
using System.Text.Json.Serialization;

namespace OlhoNoChat.Configuracoes;

/// <summary>A conta da Twitch conectada na aba Twitch, como fica no arquivo.</summary>
public sealed class ContaGravada
{
    /// <summary>O id do usuário da conta (também o canal dos resgates).</summary>
    public string Id { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string NomeDeExibicao { get; set; } = string.Empty;

    /// <summary>
    /// O acesso dado no navegador ("" sem conta). No arquivo fica protegido pelo Windows (<see cref="ProtecaoDoToken"/>).
    /// </summary>
    [JsonPropertyName("TokenProtegido")]
    [JsonConverter(typeof(ProtecaoDoToken))]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Sem o token (ou com um que só abre em outro computador ou outro usuário do Windows) não há conta: fica tudo vazio,
    /// como depois de "Desconectar", e a pessoa conecta de novo.
    /// </summary>
    public void Corrigir()
    {
        if (string.IsNullOrEmpty(Token))
        {
            Id = Login = NomeDeExibicao = Token = string.Empty;
            return;
        }

        Id ??= string.Empty;
        Login ??= string.Empty;
        NomeDeExibicao ??= string.Empty;
    }
}
