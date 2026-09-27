
namespace OlhoNoChat.Chats;

public abstract class Chat
{
    public ChatTypes ChatType { get; }

    public Chat(ChatTypes chatType)
    {
        ChatType = chatType;
    }

    public virtual string PushNewChatMessage(string message, string nick, string color)
    {
        return string.Empty;
    }

    public virtual string SetupJavascript()
    {
        return string.Empty;
    }

    public virtual string SetupCustomCSS()
    {
        return string.Empty;
    }
}
