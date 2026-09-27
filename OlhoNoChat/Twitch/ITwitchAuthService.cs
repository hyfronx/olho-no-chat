namespace OlhoNoChat.Twitch;

public interface ITwitchAuthService
{
    bool IsConnecting { get; }

    // Opens the browser and waits; returns the access token, or "" if nothing was authorized
    Task<string> ConnectAsync();

    void Cancel();
}
