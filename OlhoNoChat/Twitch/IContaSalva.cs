#nullable enable
namespace OlhoNoChat.Twitch;

/// <summary>O que fica gravado da conta da Twitch no arquivo de configurações.</summary>
public interface IContaSalva
{
    /// <summary>O acesso dado no navegador ("" sem conta).</summary>
    string Token { get; set; }

    /// <summary>O id do usuário da conta (é também o canal dos resgates).</summary>
    string Id { get; set; }

    string Login { get; set; }
    string NomeDeExibicao { get; set; }

    /// <summary>Grava o arquivo agora.</summary>
    void Gravar();
}
