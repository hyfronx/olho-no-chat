using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// The Twitch account connected in the Twitch tab. The access comes from the user's own browser
/// ("Conectar", see TwitchAuthService); here it is checked, and used to send chat messages.
/// </summary>
public class TwitchAccount
{
    public enum SendStatus { Sent, NotSent, NotConnected, Failed }
    public record SendResult(SendStatus Status, string Message = "");

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ILogger<TwitchAccount> _logger;
    private readonly Dictionary<string, string> _channelIds = new(StringComparer.OrdinalIgnoreCase);

    // Raised (on any thread) when the account is connected, checked or disconnected
    public event Action Changed;

    public TwitchAccount(ILogger<TwitchAccount> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => !string.IsNullOrEmpty(App.Settings.GeneralSettings.OAuthToken)
                               && !string.IsNullOrEmpty(App.Settings.GeneralSettings.ChannelID);

    public string DisplayName => string.IsNullOrEmpty(App.Settings.GeneralSettings.TwitchDisplayName)
        ? App.Settings.GeneralSettings.TwitchLogin
        : App.Settings.GeneralSettings.TwitchDisplayName;

    // Profile picture of the connected account (filled by CheckAsync)
    public string ProfileImageUrl { get; private set; } = string.Empty;

    public bool CanSendMessages { get; private set; } = true;

    // The emote list needs "user:read:emotes", which accesses given before version 1.0.18 don't have
    public bool CanReadEmotes { get; private set; }

    // The permissions above were read from Twitch in this run (CheckAsync)
    public bool PermissionsChecked { get; private set; }

    /// <summary>Keeps a new access from the browser: checks it and saves the account.</summary>
    public async Task<bool> ConnectAsync(string accessToken)
    {
        string previousToken = App.Settings.GeneralSettings.OAuthToken;
        App.Settings.GeneralSettings.OAuthToken = accessToken;
        bool ok = await CheckAsync(tokenChanged: previousToken != accessToken);
        if (!ok)
            Forget();
        else if (!string.IsNullOrEmpty(previousToken) && previousToken != accessToken)
            Revoke(previousToken); // connected again (e.g. for a new permission): the old access is no longer needed
        return ok;
    }

    /// <summary>
    /// Checks the saved access with Twitch (also at startup). An access that expired, was removed by
    /// the user on twitch.tv or belongs to another Twitch application is forgotten.
    /// Returns false when the account is not connected (network errors keep it connected).
    /// </summary>
    public Task<bool> CheckAsync() => CheckAsync(tokenChanged: false);

    // The settings file is written and Changed raised only when something changed
    // (the check also runs each time Configurações opens)
    private async Task<bool> CheckAsync(bool tokenChanged)
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
            using var response = await Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Twitch access is no longer valid.");
                Forget();
                return false;
            }
            response.EnsureSuccessStatusCode();

            var info = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            if ((string)info?["client_id"] != AppInfo.TwitchClientId)
            {
                _logger.LogInformation("Saved Twitch access belongs to another application; forgetting it.");
                Forget();
                return false;
            }

            var settings = App.Settings.GeneralSettings;
            var savedBefore = (settings.ChannelID, settings.TwitchLogin, settings.TwitchDisplayName);
            var stateBefore = (ProfileImageUrl, CanSendMessages, CanReadEmotes, PermissionsChecked);

            settings.ChannelID = (string)info["user_id"] ?? string.Empty;
            settings.TwitchLogin = (string)info["login"] ?? string.Empty;
            var scopes = info["scopes"] as JsonArray;
            CanSendMessages = scopes != null && scopes.Any(s => (string)s == "user:write:chat");
            CanReadEmotes = scopes != null && scopes.Any(s => (string)s == "user:read:emotes");
            PermissionsChecked = true;

            await FetchProfileAsync(token);

            bool savedChanged = tokenChanged || savedBefore != (settings.ChannelID, settings.TwitchLogin, settings.TwitchDisplayName);
            if (savedChanged)
                App.Settings.Persist();
            if (savedChanged || stateBefore != (ProfileImageUrl, CanSendMessages, CanReadEmotes, PermissionsChecked))
                Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            // No internet or Twitch unavailable: keep the account, try again later
            _logger.LogWarning(ex, "Could not check the Twitch access.");
            return IsConnected;
        }
    }

    private async Task FetchProfileAsync(string token)
    {
        using var request = TwitchApiRequest(HttpMethod.Get, "https://api.twitch.tv/helix/users", token);
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return;

        var user = JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"]?[0];
        if (user == null)
            return;

        App.Settings.GeneralSettings.TwitchDisplayName = (string)user["display_name"] ?? string.Empty;
        ProfileImageUrl = (string)user["profile_image_url"] ?? string.Empty;
    }

    /// <summary>Disconnects the account: the access is also cancelled on Twitch.</summary>
    public void Disconnect()
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        Forget();
        Revoke(token);
    }

    // Cancels an access on Twitch (in the background)
    private void Revoke(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var body = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = AppInfo.TwitchClientId,
                        ["token"] = token
                    });
                    using var response = await Http.PostAsync("https://id.twitch.tv/oauth2/revoke", body);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not revoke the Twitch access.");
                }
            });
        }
    }

    private void Forget()
    {
        var settings = App.Settings.GeneralSettings;
        settings.OAuthToken = string.Empty;
        settings.ChannelID = string.Empty;
        settings.TwitchLogin = string.Empty;
        settings.TwitchDisplayName = string.Empty;
        ProfileImageUrl = string.Empty;
        CanReadEmotes = false;
        PermissionsChecked = false;
        App.Settings.Persist();
        Changed?.Invoke();
    }

    /// <summary>Sends a message to the chat of the channel (login name) as the connected account.</summary>
    public async Task<SendResult> SendChatMessageAsync(string channelLogin, string message)
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        string senderId = App.Settings.GeneralSettings.ChannelID;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(senderId))
            return new SendResult(SendStatus.NotConnected, "Conecte sua conta da Twitch nas Configurações (aba Twitch).");

        try
        {
            string broadcasterId = await GetChannelIdAsync(channelLogin, token);
            if (broadcasterId == null)
                return new SendResult(SendStatus.Failed, $"Não achei o canal \"{channelLogin}\" na Twitch.");

            string json = JsonSerializer.Serialize(new { broadcaster_id = broadcasterId, sender_id = senderId, message });
            using var request = TwitchApiRequest(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages", token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var result = JsonNode.Parse(body)?["data"]?[0];
                    if ((bool?)result?["is_sent"] == true)
                        return new SendResult(SendStatus.Sent);
                    string reason = (string)result?["drop_reason"]?["message"];
                    return new SendResult(SendStatus.NotSent, string.IsNullOrWhiteSpace(reason)
                        ? "A Twitch não publicou a mensagem."
                        : $"A Twitch não publicou a mensagem: {reason}");

                case HttpStatusCode.Unauthorized:
                    _ = CheckAsync();
                    return new SendResult(SendStatus.NotConnected, "A conexão com a Twitch expirou. Conecte de novo nas Configurações (aba Twitch).");

                case HttpStatusCode.Forbidden:
                    return new SendResult(SendStatus.NotSent, "A Twitch não deixou enviar nesse chat (você pode estar banido ou suspenso nele).");

                case HttpStatusCode.TooManyRequests:
                    return new SendResult(SendStatus.NotSent, "Muitas mensagens seguidas. Espere um pouco e tente de novo.");

                default:
                    _logger.LogWarning("Send chat message failed: {Status} {Body}", response.StatusCode, body);
                    string error = null;
                    try { error = (string)JsonNode.Parse(body)?["message"]; } catch (JsonException) { }
                    return new SendResult(SendStatus.Failed, $"Não foi possível enviar ({(int)response.StatusCode}{(string.IsNullOrEmpty(error) ? "" : ": " + error)}).");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send chat message failed.");
            return new SendResult(SendStatus.Failed, "Não foi possível enviar. Confira sua internet.");
        }
    }

    /// <summary>Whether the channel (login name) exists on Twitch; null when it could not be checked.</summary>
    public async Task<bool?> ChannelExistsAsync(string channelLogin)
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            return await GetChannelIdAsync(channelLogin, token) != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check whether the channel exists.");
            return null;
        }
    }

    /// <summary>
    /// Subscribes an EventSub websocket session (see TwitchService) to the channel point redemptions of the
    /// connected account's channel. Returns false (the reason is logged) when Twitch refuses or can't be reached.
    /// </summary>
    public async Task<bool> SubscribeToRedemptionsAsync(string sessionId)
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        string userId = App.Settings.GeneralSettings.ChannelID;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            return false;

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                type = "channel.channel_points_custom_reward_redemption.add",
                version = "1",
                condition = new { broadcaster_user_id = userId, moderator_user_id = userId },
                transport = new { method = "websocket", session_id = sessionId }
            });
            using var request = TwitchApiRequest(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions", token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Accepted)
                return true;

            _logger.LogWarning("EventSub subscribe failed: {Status} {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
            // The access may have expired or been removed (checked on the UI thread, like the other checks)
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                _ = App.Current.Dispatcher.BeginInvoke(new Action(() => _ = CheckAsync()));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EventSub subscribe failed.");
            return false;
        }
    }

    public record Emote(string Id, string Name);
    public record EmoteGroup(string Title, IReadOnlyList<Emote> Emotes);

    /// <param name="OnlyGlobal">The access doesn't allow reading the account's emotes: only the global ones are listed.</param>
    public record EmoteList(IReadOnlyList<EmoteGroup> Groups, bool OnlyGlobal);

    // Emote names that are words; the old smileys (":)", "<3", "R-)", "O_o") are left out, emojis do that better
    private static readonly System.Text.RegularExpressions.Regex EmoteName = new(@"^\w+$");
    private static readonly HashSet<string> OldSmileys = new(StringComparer.OrdinalIgnoreCase) { "O_o", "o_O", "O_O", "o_o" };

    /// <summary>
    /// The emotes the connected account can use in the chat of the channel: the channel's, the ones from
    /// the account's subscriptions and the global ones, grouped by channel. Throws when Twitch can't be reached.
    /// </summary>
    public async Task<EmoteList> GetEmotesAsync(string channelLogin)
    {
        string token = App.Settings.GeneralSettings.OAuthToken;
        string userId = App.Settings.GeneralSettings.ChannelID;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
            return new EmoteList(Array.Empty<EmoteGroup>(), OnlyGlobal: true);

        string channelId = string.IsNullOrEmpty(channelLogin) ? null : await GetChannelIdAsync(channelLogin, token);
        var found = new List<(string Id, string Name, string Owner)>();
        bool onlyGlobal = !CanReadEmotes;

        if (!onlyGlobal)
        {
            string cursor = null;
            for (int page = 0; page < 30; page++)
            {
                string url = "https://api.twitch.tv/helix/chat/emotes/user?user_id=" + userId
                    + (channelId != null ? "&broadcaster_id=" + channelId : string.Empty)
                    + (cursor != null ? "&after=" + Uri.EscapeDataString(cursor) : string.Empty);
                using var request = TwitchApiRequest(HttpMethod.Get, url, token);
                using var response = await Http.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Permission missing (or access expired): check the access again and show the global ones
                    _logger.LogInformation("Reading the account's emotes was refused: {Body}", await response.Content.ReadAsStringAsync());
                    _ = CheckAsync();
                    onlyGlobal = true;
                    found.Clear();
                    break;
                }
                response.EnsureSuccessStatusCode();

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                foreach (var emote in json?["data"]?.AsArray() ?? new JsonArray())
                {
                    if ((string)emote?["emote_type"] != "smilies")
                        found.Add(((string)emote?["id"], (string)emote?["name"], (string)emote?["owner_id"]));
                }

                cursor = (string)json?["pagination"]?["cursor"];
                if (string.IsNullOrEmpty(cursor))
                    break;
            }
        }

        if (onlyGlobal)
        {
            using var request = TwitchApiRequest(HttpMethod.Get, "https://api.twitch.tv/helix/chat/emotes/global", token);
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var global = (json?["data"]?.AsArray() ?? new JsonArray()).Select(e => ((string)e?["id"], (string)e?["name"], "0")).ToList();
            global.Reverse(); // Twitch lists the newest first; the classic ones (Kappa...) are the most used
            found.AddRange(global);
        }

        // One group per channel: this channel first, then the others by name, the global ones at the end
        var usable = found.Where(e => !string.IsNullOrEmpty(e.Id) && e.Name != null && EmoteName.IsMatch(e.Name) && !OldSmileys.Contains(e.Name))
                          .DistinctBy(e => e.Name)
                          .ToList();
        static bool IsGlobal(string owner) => string.IsNullOrEmpty(owner) || owner == "0" || owner == "twitch";

        var ownerIds = usable.Select(e => e.Owner).Where(o => !IsGlobal(o)).Distinct().ToList();
        var ownerNames = await GetDisplayNamesAsync(ownerIds, token);

        var groups = new List<EmoteGroup>();
        foreach (var byOwner in usable.Where(e => !IsGlobal(e.Owner)).GroupBy(e => e.Owner)
                                      .OrderBy(g => g.Key == channelId ? 0 : 1)
                                      .ThenBy(g => ownerNames.GetValueOrDefault(g.Key, g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            string name = ownerNames.GetValueOrDefault(byOwner.Key, byOwner.Key == channelId ? channelLogin : "Outro canal");
            groups.Add(new EmoteGroup(byOwner.Key == channelId ? $"{name} (este canal)" : name,
                                      byOwner.Select(e => new Emote(e.Id, e.Name)).ToList()));
        }

        var globalEmotes = usable.Where(e => IsGlobal(e.Owner)).Select(e => new Emote(e.Id, e.Name)).ToList();
        if (globalEmotes.Count > 0)
            groups.Add(new EmoteGroup("Globais da Twitch", globalEmotes));

        return new EmoteList(groups, onlyGlobal);
    }

    // Display names of channels (for the emote groups); missing ones are left out
    private async Task<Dictionary<string, string>> GetDisplayNamesAsync(IReadOnlyList<string> userIds, string token)
    {
        var names = new Dictionary<string, string>();
        foreach (var batch in userIds.Chunk(100))
        {
            try
            {
                string url = "https://api.twitch.tv/helix/users?" + string.Join("&", batch.Select(id => "id=" + Uri.EscapeDataString(id)));
                using var request = TwitchApiRequest(HttpMethod.Get, url, token);
                using var response = await Http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                foreach (var user in json?["data"]?.AsArray() ?? new JsonArray())
                {
                    string id = (string)user?["id"];
                    string name = (string)user?["display_name"];
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        names[id] = name;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get the names of the emote channels.");
            }
        }
        return names;
    }

    private async Task<string> GetChannelIdAsync(string channelLogin, string token)
    {
        if (_channelIds.TryGetValue(channelLogin, out string id))
            return id;

        using var request = TwitchApiRequest(HttpMethod.Get, "https://api.twitch.tv/helix/users?login=" + Uri.EscapeDataString(channelLogin), token);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // An empty list: no channel with that name
        var users = JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"] as JsonArray;
        id = users is { Count: > 0 } ? (string)users[0]?["id"] : null;
        if (id != null)
            _channelIds[channelLogin] = id;
        return id;
    }

    private static HttpRequestMessage TwitchApiRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Client-Id", AppInfo.TwitchClientId);
        return request;
    }
}
