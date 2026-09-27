namespace OlhoNoChat.Chats;

/// <summary>
/// The welcome page (browser/index.html): none of the chat CSS or scripts. Its chat type is CustomURL,
/// so the checks for the channel chats ("Padrão", "Chat oficial da Twitch") leave it alone.
/// </summary>
public class WelcomeChat : Chat
{
    public WelcomeChat() : base(ChatTypes.CustomURL)
    {
    }
}
