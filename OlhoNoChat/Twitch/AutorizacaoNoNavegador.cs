using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Twitch;

/// <summary>
/// "Conectar": abre a página de autorização da Twitch no navegador padrão da pessoa (onde ela normalmente já está
/// logada, então é só clicar em "Autorizar") e espera o acesso voltar num servidor local.
/// </summary>
/// <remarks>
/// A Twitch devolve o acesso depois do "#" do endereço, que o navegador não manda ao servidor: a página de resposta
/// lê o "#" e repassa para "/callback". Os endereços de retorno estão registrados no app da Twitch, porta por porta.
/// </remarks>
public sealed class AutorizacaoNoNavegador
{
    public enum Fim { Token, SemToken, PortasOcupadas, JaEmAndamento }

    /// <param name="Token">O acesso, quando <paramref name="Fim"/> é <see cref="Fim.Token"/>.</param>
    public sealed record Resultado(Fim Fim, string Token = "");

    public const string TextoPortasOcupadas = "Não foi possível esperar a resposta da Twitch: outros programas estão usando as portas do computador que o Olho no Chat usa. Tente de novo daqui a pouco.";

    /// <summary>Portas tentadas em ordem (as outras ficam fora das faixas que o Windows dá a outros programas).</summary>
    public static readonly IReadOnlyList<int> Portas = [8981, 28981, 38981, 45981];

    private readonly Action<string> _abrirNoNavegador;
    private readonly IReadOnlyList<int> _portas;
    private readonly TimeSpan _limite;
    private readonly ILogger<AutorizacaoNoNavegador> _log;
    private CancellationTokenSource? _espera;

    public AutorizacaoNoNavegador(ILogger<AutorizacaoNoNavegador> log)
        : this(log, AbrirNoWindows.Site, Portas, TimeSpan.FromMinutes(5)) { }

    /// <summary>Os testes trocam o navegador, as portas e o tempo limite.</summary>
    public AutorizacaoNoNavegador(ILogger<AutorizacaoNoNavegador> log, Action<string> abrirNoNavegador,
        IReadOnlyList<int> portas, TimeSpan limite)
    {
        _log = log;
        _abrirNoNavegador = abrirNoNavegador;
        _portas = portas;
        _limite = limite;
    }

    public bool EstaEsperando => _espera != null;

    /// <summary>Encerra a espera (termina sem token).</summary>
    public void Cancelar() => _espera?.Cancel();

    public async Task<Resultado> ConectarAsync()
    {
        if (_espera != null)
            return new Resultado(Fim.JaEmAndamento);

        if (AbrirServidor() is not { } aberto)
            return new Resultado(Fim.PortasOcupadas);
        var (servidor, porta) = aberto;

        using var espera = new CancellationTokenSource(_limite);
        _espera = espera;
        try
        {
            string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _abrirNoNavegador(EnderecoDeAutorizacao(porta, state));
            string? token = await EsperarRetornoAsync(servidor, state, espera.Token);
            return token != null ? new Resultado(Fim.Token, token) : new Resultado(Fim.SemToken);
        }
        finally
        {
            _espera = null;
            servidor.Close();
        }
    }

    public static string EnderecoDeRetorno(int porta) => $"http://localhost:{porta}/auth";

    public static string EnderecoDeAutorizacao(int porta, string state) =>
        "https://id.twitch.tv/oauth2/authorize?response_type=token"
        + "&client_id=" + InfoDoApp.TwitchClientId
        + "&redirect_uri=" + Uri.EscapeDataString(EnderecoDeRetorno(porta))
        + "&scope=" + Uri.EscapeDataString(InfoDoApp.PermissoesDaTwitch)
        + "&force_verify=true" // a Twitch sempre mostra a página, mesmo para quem já autorizou
        + "&state=" + state;

    /// <summary>
    /// O que chegou em "/callback": se é a resposta deste "Conectar" (o state confere) e, se for, o acesso (null
    /// quando a pessoa recusou ou deu erro).
    /// </summary>
    public static (bool DesteConectar, string? Token) LerRetorno(string query, string state)
    {
        var parametros = HttpUtility.ParseQueryString(query);
        if (parametros["state"] != state)
            return (false, null);
        string? token = parametros["access_token"];
        return (true, string.IsNullOrEmpty(token) ? null : token);
    }

    private (HttpListener, int)? AbrirServidor()
    {
        foreach (int porta in _portas)
        {
            var servidor = new HttpListener();
            servidor.Prefixes.Add($"http://localhost:{porta}/");
            try
            {
                servidor.Start();
                return (servidor, porta);
            }
            catch (HttpListenerException ex)
            {
                _log.LogInformation("Porta {Porta} ocupada: {Erro}", porta, ex.Message);
                servidor.Close();
            }
        }
        return null;
    }

    private async Task<string?> EsperarRetornoAsync(HttpListener servidor, string state, CancellationToken cancelar)
    {
        // Fechar o servidor faz a espera por um pedido terminar
        using CancellationTokenRegistration aoCancelar = cancelar.Register(servidor.Stop);
        while (!cancelar.IsCancellationRequested)
        {
            HttpListenerContext pedido;
            try
            {
                pedido = await servidor.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return null;
            }

            string caminho = pedido.Request.Url?.AbsolutePath ?? string.Empty;
            if (caminho == "/auth")
            {
                Responder(pedido.Response, 200, "text/html; charset=utf-8", PaginaDeResposta);
            }
            else if (caminho == "/callback")
            {
                Responder(pedido.Response, 200, "text/plain; charset=utf-8", "ok");
                var (desteConectar, token) = LerRetorno(pedido.Request.Url?.Query ?? string.Empty, state);
                if (desteConectar)
                    return token;
            }
            else
            {
                Responder(pedido.Response, 404, "text/plain; charset=utf-8", "");
            }
        }
        return null;
    }

    private static void Responder(HttpListenerResponse resposta, int codigo, string tipo, string corpo)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(corpo);
            resposta.StatusCode = codigo;
            resposta.ContentType = tipo;
            resposta.Headers["Cache-Control"] = "no-store";
            resposta.ContentLength64 = bytes.Length;
            resposta.OutputStream.Write(bytes);
            resposta.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // O navegador fechou a aba antes da resposta
        }
    }

    // O resultado vem depois do "#" (ou em "?error=..." quando a pessoa recusa). A página repassa para "/callback"
    // e tira o acesso da barra de endereço, para ele não ficar no histórico do navegador.
    private const string PaginaDeResposta = """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Olho no Chat</title>
        <style>
          body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                 background: #1c1c1c; color: #eeeeee; font-family: "Segoe UI", Arial, sans-serif; }
          .cartao { background: #282828; border-top: 6px solid #E85D30; border-radius: 10px; padding: 28px 32px;
                    max-width: 420px; margin: 16px; box-shadow: 0 8px 24px rgba(0, 0, 0, .4); }
          h1 { margin: 0 0 10px; font-size: 22px; }
          p { margin: 0; font-size: 15px; line-height: 1.5; color: #cccccc; }
        </style>
        </head>
        <body>
        <div class="cartao"><h1 id="titulo"></h1><p id="texto"></p></div>
        <script>
          var recebido = location.hash.length > 1 ? location.hash.substring(1) : location.search.substring(1);
          var conectou = new URLSearchParams(recebido).has("access_token");
          document.getElementById("titulo").textContent = conectou ? "Conta conectada!" : "Conexão cancelada";
          document.getElementById("texto").textContent = conectou
            ? "Pode fechar esta aba e voltar para o Olho no Chat."
            : "Nada foi conectado. Se quiser tentar de novo, clique em Conectar no Olho no Chat.";
          history.replaceState(null, "", "/auth");
          fetch("/callback?" + recebido).catch(function () {});
        </script>
        </body>
        </html>
        """;
}
