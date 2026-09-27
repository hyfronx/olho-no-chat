using Microsoft.Extensions.Logging;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Channel point redemptions of the account connected in the Twitch tab (EventSub websocket).
/// Created by MainWindow only once redemptions are turned on.
/// </summary>
public class TwitchService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TwitchService> _logger;
    private readonly TwitchAccount _account;

    // Created on first use and kept for later enable/disable cycles
    private EventSubWebsocketClient _eventSubWebsocketClient;

    public event EventHandler<ChannelPointsCustomRewardRedemptionArgs> ChannelPointsRewardRedeemed;

    // Started from the UI thread; the websocket events and the reconnect loop run on other threads
    private readonly object _stateLock = new();
    private bool _isEventSubInit = false;

    // Changes on every start and stop, so the retries of an older connection give up
    private int _connectionGeneration = 0;

    // Reconnection strategy parameters
    private const int MaxReconnectAttempts = 7;  // Maximum number of times to try reconnecting
    private const int BaseDelayMilliseconds = 1000; // Initial delay: 1 second
    private const int MaxDelayMilliseconds = 60000; // Maximum delay: 1 minute

    public TwitchService(ILoggerFactory loggerFactory, TwitchAccount account)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _logger = loggerFactory.CreateLogger<TwitchService>();
    }

    // Also offline (e.g. the app started without internet): a failed connection starts over on the next
    // chat page load or "Salvar" (see InitializeAsync), since no disconnection is ever reported for it
    private async Task ConnectEventSubAsync(int generation)
    {
        _logger.LogInformation("Connecting EventSub for user {UserId}...", App.Settings.GeneralSettings.ChannelID);

        bool connected;
        try
        {
            connected = await _eventSubWebsocketClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect EventSub.");
            connected = false;
        }

        if (!connected)
        {
            _logger.LogWarning("EventSub did not connect. It is tried again when the chat page reloads or on Salvar.");
            ResetEventSub(generation);
        }
    }

    // After a connection that could not be made or kept: the next InitializeAsync starts over
    private void ResetEventSub(int generation)
    {
        lock (_stateLock)
        {
            if (generation != _connectionGeneration)
                return; // stopped or started again meanwhile

            _isEventSubInit = false;
            UnsubscribeFromEvents();
        }
    }

    private async Task DisconnectEventSubAsync()
    {
        _logger.LogInformation("Stopping _eventSubWebsocketClient...");
        await _eventSubWebsocketClient.DisconnectAsync();
    }

    // Starts the channel point redemptions of the account connected in the Twitch tab (checked by TwitchAccount)
    public Task InitializeAsync()
    {
        _logger.LogInformation("InitializeAsync()");
        if (!string.IsNullOrEmpty(App.Settings.GeneralSettings.OAuthToken))
            InitEventSub();
        return Task.CompletedTask;
    }

    private void InitEventSub()
    {
        int generation;
        lock (_stateLock)
        {
            if (_isEventSubInit) return; // Already initialized
            if (App.Settings.GeneralSettings.RedemptionsEnabled == false)
            {
                _logger.LogInformation("EventSub is disabled in settings. Skipping initialization.");
                return;
            }
            if (string.IsNullOrEmpty(App.Settings.GeneralSettings.ChannelID))
            {
                _logger.LogWarning("Channel ID is not set in App.Settings.GeneralSettings. Cannot initialize EventSub.");
                return;
            }
            if (string.IsNullOrEmpty(App.Settings.GeneralSettings.OAuthToken))
            {
                _logger.LogWarning("OAuth Token is not set in App.Settings.GeneralSettings. Cannot initialize EventSub.");
                return;
            }

            _logger.LogInformation("Initializing Twitch EventSub WebSocket client for channel: {ChannelId}", App.Settings.GeneralSettings.ChannelID);

            _eventSubWebsocketClient ??= new EventSubWebsocketClient(_loggerFactory);

            // ensure no old subscriptions are hanging around
            UnsubscribeFromEvents();

            _eventSubWebsocketClient.WebsocketConnected += OnWebsocketConnected;
            _eventSubWebsocketClient.WebsocketDisconnected += OnWebsocketDisconnected;
            _eventSubWebsocketClient.WebsocketReconnected += OnWebsocketReconnected;
            _eventSubWebsocketClient.ErrorOccurred += OnErrorOccurred;
            _eventSubWebsocketClient.ChannelPointsCustomRewardRedemptionAdd += OnChannelPointsCustomRewardRedemptionAdd;

            _isEventSubInit = true;
            generation = ++_connectionGeneration;
        }

        _ = ConnectEventSubAsync(generation);
    }

    public void DisableEventSub()
    {
        lock (_stateLock)
        {
            if (!_isEventSubInit)
                return; // not started

            _logger.LogInformation("User requested disabling Event Sub.");

            // Also stops a reconnect loop that is running
            _connectionGeneration++;
            _isEventSubInit = false;
            UnsubscribeFromEvents();
        }

        // Stop the active service (which disconnects the websocket)
        _ = DisconnectEventSubAsync();
    }

    private Task OnChannelPointsCustomRewardRedemptionAdd(object sender, ChannelPointsCustomRewardRedemptionArgs e)
    {
        var eventData = e.Notification.Payload.Event;
        _logger.LogInformation("ChannelPointsCustomRewardRedemptionAdd: {Title} redeemed by {User} ({Cost})", eventData.Reward.Title, eventData.UserName, eventData.Reward.Cost);
        if (!string.IsNullOrWhiteSpace(eventData.UserInput))
            _logger.LogInformation("User Input: {UserInput}", eventData.UserInput);

        ChannelPointsRewardRedeemed?.Invoke(this, e);
        return Task.CompletedTask;
    }

    // --- WebSocket Events -------------------------------------------------------------
    private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
    {
        _logger.LogInformation("Websocket {SessionId} connected!", _eventSubWebsocketClient.SessionId);

        // A new session starts with no subscriptions (a reconnect requested by Twitch keeps them)
        if (!e.IsRequestedReconnect && !await _account.SubscribeToRedemptionsAsync(_eventSubWebsocketClient.SessionId))
            _logger.LogWarning("Websocket {SessionId}: the redemptions subscription failed.", _eventSubWebsocketClient.SessionId);
    }

    private async Task OnWebsocketDisconnected(object sender, EventArgs e)
    {
        _logger.LogWarning("[Twitch EventSub] Websocket {SessionId} disconnected!", _eventSubWebsocketClient.SessionId);
        int generation = _connectionGeneration;

        for (int attempt = 0; attempt < MaxReconnectAttempts; attempt++)
        {
            // Calculate exponential backoff delay
            // Formula: BaseDelay * (2^attempt)
            double exponentialDelay = BaseDelayMilliseconds * Math.Pow(2, attempt);

            // Add jitter: a random small duration (e.g., 0 to 1 second) to prevent thundering herd
            int jitter = Random.Shared.Next(0, 1000);

            // Calculate total delay, ensuring it doesn't exceed MaxDelayMilliseconds
            int delayMilliseconds = (int)Math.Min(exponentialDelay + jitter, MaxDelayMilliseconds);

            _logger.LogInformation("[Twitch EventSub] Reconnect attempt {Attempt}/{MaxAttempts}. Waiting {Delay}ms before next attempt...", attempt + 1, MaxReconnectAttempts, delayMilliseconds);
            await Task.Delay(delayMilliseconds);

            // Redemptions turned off (or started again) while waiting
            if (generation != _connectionGeneration)
                return;

            try
            {
                _logger.LogInformation("[Twitch EventSub] Attempting to reconnect (Attempt {Attempt})...", attempt + 1);
                if (await _eventSubWebsocketClient.ReconnectAsync())
                {
                    _logger.LogInformation("[Twitch EventSub] Websocket {SessionId} reconnected successfully on attempt {Attempt}!", _eventSubWebsocketClient.SessionId, attempt + 1);
                    return; // Successfully reconnected, exit the method.
                }
                else
                {
                    _logger.LogInformation("[Twitch EventSub] Websocket {SessionId} reconnect attempt {Attempt} failed.", _eventSubWebsocketClient.SessionId, attempt + 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "[Twitch EventSub] Websocket {SessionId} reconnect attempt {Attempt} threw an exception", _eventSubWebsocketClient.SessionId, attempt + 1);
            }
        }

        _logger.LogWarning("[Twitch EventSub] Websocket {SessionId} failed to reconnect after {MaxAttempts} attempts. It is tried again when the chat page reloads or on Salvar.", _eventSubWebsocketClient.SessionId, MaxReconnectAttempts);
        ResetEventSub(generation);
    }

    private Task OnWebsocketReconnected(object sender, EventArgs e)
    {
        _logger.LogInformation("Websocket {SessionId} reconnected", _eventSubWebsocketClient.SessionId);
        return Task.CompletedTask;
    }

    private Task OnErrorOccurred(object sender, ErrorOccuredArgs e)
    {
        _logger.LogInformation(e.Exception, "Websocket {SessionId} - Error occurred: {Message}", _eventSubWebsocketClient.SessionId, e.Message);
        return Task.CompletedTask;
    }

    // --- Cleanup Methods -------------------------------------------------------------

    private void UnsubscribeFromEvents()
    {
        _logger.LogTrace("Unsubscribing from TwitchService events.");

        if (_eventSubWebsocketClient != null)
        {
            _eventSubWebsocketClient.WebsocketConnected -= OnWebsocketConnected;
            _eventSubWebsocketClient.WebsocketDisconnected -= OnWebsocketDisconnected;
            _eventSubWebsocketClient.WebsocketReconnected -= OnWebsocketReconnected;
            _eventSubWebsocketClient.ErrorOccurred -= OnErrorOccurred;
            _eventSubWebsocketClient.ChannelPointsCustomRewardRedemptionAdd -= OnChannelPointsCustomRewardRedemptionAdd;
        }
    }
}
