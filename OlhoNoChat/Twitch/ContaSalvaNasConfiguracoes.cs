#nullable enable
namespace OlhoNoChat.Twitch;

/// <summary>A conta dentro das configurações de hoje (chaves OAuthToken, ChannelID, TwitchLogin, TwitchDisplayName).</summary>
public sealed class ContaSalvaNasConfiguracoes : IContaSalva
{
    private static GeneralSettings Opcoes => App.Settings.GeneralSettings;

    public string Token { get => Opcoes.OAuthToken ?? string.Empty; set => Opcoes.OAuthToken = value; }
    public string Id { get => Opcoes.ChannelID ?? string.Empty; set => Opcoes.ChannelID = value; }
    public string Login { get => Opcoes.TwitchLogin ?? string.Empty; set => Opcoes.TwitchLogin = value; }
    public string NomeDeExibicao { get => Opcoes.TwitchDisplayName ?? string.Empty; set => Opcoes.TwitchDisplayName = value; }

    public void Gravar() => App.Settings.Persist();
}
