using OlhoNoChat.Configuracoes;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Chat;

/// <summary>
/// O "Chat Multiplataforma": o chat da Twitch, o de uma live do YouTube, o de um canal da Kick e o de uma live do TikTok
/// juntos no Padrão. Ligado, o YouTube, a Kick e o TikTok são só leitura e os recursos só da Twitch ficam desligados para não misturar as plataformas: escrever no chat (e os emotes da conta),
/// os resgates de pontos e os outros tipos de chat. A conta da Twitch continua conectada, só sem uso.
/// </summary>
public static class ChatMultiplataforma
{
    public const string TextoSemEscrever =
        "No Chat Multiplataforma o chat é só para ler. Para escrever, desligue o Chat Multiplataforma em Configurações > Chat.";

    /// <summary>Os resgates de pontos aparecem (opção da aba Twitch, desligada no Chat Multiplataforma).</summary>
    public static bool ResgatesLigados(Opcoes opcoes) => opcoes.MostrarResgates && !opcoes.ChatMultiplataforma;

    /// <summary>O canal do YouTube a ler, ou null (função desligada ou sem canal).</summary>
    public static CanalDoYouTube? CanalALer(Opcoes opcoes) =>
        opcoes.ChatMultiplataforma ? CanalDoYouTube.Ler(opcoes.CanalDoYouTube) : null;

    /// <summary>Ligado, com o canal do YouTube, o da Kick ou o do TikTok: o chat abre mesmo sem canal da Twitch.</summary>
    public static bool TemOutroCanal(Opcoes opcoes) =>
        CanalALer(opcoes) != null || KickALer(opcoes) != null || TikTokALer(opcoes) != null;

    /// <summary>O canal da Kick a ler (o nome do endereço), ou null (função desligada ou sem canal).</summary>
    public static string? KickALer(Opcoes opcoes) =>
        opcoes.ChatMultiplataforma ? CanalDaKick.Ler(opcoes.CanalDaKick) : null;

    /// <summary>O canal do TikTok a ler (sem o @), ou null (função desligada ou sem canal).</summary>
    public static string? TikTokALer(Opcoes opcoes) =>
        opcoes.ChatMultiplataforma ? CanalDoTikTok.Ler(opcoes.CanalDoTikTok) : null;
}
