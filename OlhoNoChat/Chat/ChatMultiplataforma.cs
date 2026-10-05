using OlhoNoChat.Configuracoes;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Chat;

/// <summary>
/// O "Chat Multiplataforma": o chat da Twitch e o de uma live do YouTube juntos no Padrão. Ligado, o YouTube é só leitura e
/// os recursos só da Twitch ficam desligados para não misturar as plataformas: escrever no chat (e os emotes da conta),
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
}
