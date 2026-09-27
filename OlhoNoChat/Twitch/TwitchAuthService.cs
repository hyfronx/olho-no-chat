using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;
using OlhoNoChat.Utils;

namespace OlhoNoChat.Twitch;

/// <summary>
/// "Conectar": opens the Twitch authorization page in the user's default browser (if they are already
/// logged in there, one click on "Autorizar" is enough). Twitch then sends the browser back to a small
/// page served here on localhost, which hands the access token to the app.
/// </summary>
public class TwitchAuthService : ITwitchAuthService
{
    // The page's address is http://localhost:<port>/auth, registered for the app at dev.twitch.tv for each of these
    // ports. They are tried in order: Windows may have handed one of them to another program's connection (on some
    // computers its temporary ports start at 1024). The others are outside the usual ranges of temporary ports.
    private static readonly int[] Ports = { 8981, 28981, 38981, 45981 };
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(5);

    private HttpListener _listener;
    private string _state = string.Empty;
    private CancellationTokenSource _cancel;

    public bool IsConnecting => _cancel != null;

    /// <summary>
    /// Opens the browser and waits for the authorization. Returns the access token, or an empty
    /// string when the user refused, cancelled or took too long. Throws HttpListenerException if all
    /// the local ports are busy.
    /// </summary>
    public async Task<string> ConnectAsync()
    {
        if (_cancel != null)
            return string.Empty;

        _cancel = new CancellationTokenSource(WaitLimit);
        try
        {
            _state = Guid.NewGuid().ToString("N");

            string redirectUri = StartListener();
            using var abort = _cancel.Token.Register(() => _listener?.Abort());

            LaunchBrowser(_state, redirectUri);
            return await ListenAsync(_cancel.Token);
        }
        finally
        {
            try { _listener?.Close(); } catch (ObjectDisposedException) { }
            _listener = null;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    /// <summary>Stops waiting for the browser.</summary>
    public void Cancel()
    {
        try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
    }

    // Listens on the first free port of Ports and returns the page's address on it
    private string StartListener()
    {
        for (int i = 0; ; i++)
        {
            string prefix = $"http://localhost:{Ports[i]}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            try
            {
                _listener.Start();
                return prefix + "auth";
            }
            catch (HttpListenerException) when (i < Ports.Length - 1)
            {
                _listener.Close();
            }
        }
    }

    private static void LaunchBrowser(string state, string redirectUri)
    {
        string url = "https://id.twitch.tv/oauth2/authorize?response_type=token"
            + "&client_id=" + AppInfo.TwitchClientId
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&scope=" + Uri.EscapeDataString(AppInfo.TwitchScopes)
            + "&force_verify=true"
            + "&state=" + state;

        ShellHelper.OpenUrl(url);
    }

    private async Task<string> ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // 1. Twitch sends the browser here, with the result after "#" (only the page can read it)
            if (request.Url.AbsolutePath == "/auth")
            {
                byte[] buffer = Encoding.UTF8.GetBytes(AuthPage);
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            // 2. The page sends that result here
            else if (request.Url.AbsolutePath == "/callback")
            {
                var query = HttpUtility.ParseQueryString(request.Url.Query);
                response.StatusCode = 200;
                response.Close();

                if (query["state"] != _state)
                    continue; // not from the page this app opened

                string token = query["access_token"];
                if (string.IsNullOrEmpty(token))
                    Debug.WriteLine("Twitch authorization refused: " + query["error"]);
                return token ?? string.Empty;
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }

        return string.Empty;
    }

    private const string AuthPage = """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <title>Olho no Chat</title>
        <style>
            body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                   background: #1c1c1c; color: #e6e6e6; font-family: "Segoe UI", Arial, sans-serif; text-align: center; }
            .card { max-width: 440px; padding: 36px 40px; background: #282828; border: 1px solid #333; border-radius: 12px; }
            .bar { height: 6px; margin: -36px -40px 28px; background: #E85D30; border-radius: 12px 12px 0 0; }
            h1 { font-size: 22px; margin: 0 0 12px; color: #fff; }
            p { font-size: 16px; line-height: 1.5; margin: 0; }
        </style>
        </head>
        <body>
        <div class="card">
            <div class="bar"></div>
            <h1 id="title">Olho no Chat</h1>
            <p id="text"></p>
        </div>
        <script>
            var result = new URLSearchParams(location.hash.substring(1));
            var ok = result.has('access_token');
            document.getElementById('title').textContent = ok ? 'Conta conectada!' : 'Conexão cancelada';
            document.getElementById('text').textContent = ok
                ? 'Pode fechar esta aba e voltar para o Olho no Chat.'
                : 'Nada foi conectado. Se quiser tentar de novo, clique em Conectar no Olho no Chat.';
            if (location.hash.length > 1 || location.search.length > 1) {
                var params = new URLSearchParams(location.hash.length > 1 ? location.hash.substring(1) : location.search.substring(1));
                fetch('/callback?' + params.toString()).catch(function () {});
            }
            history.replaceState(null, '', '/auth');
        </script>
        </body>
        </html>
        """;
}
