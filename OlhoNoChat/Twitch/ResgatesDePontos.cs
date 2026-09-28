#nullable enable
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Os resgates de pontos do canal da conta conectada, pelo EventSub da Twitch num websocket: a Twitch dá boas-vindas
/// com o id da sessão, o app assina os resgates nessa sessão e a Twitch manda cada resgate, com um "estou vivo" nos
/// intervalos. Se a conexão cai, tenta de novo até 7 vezes.
/// </summary>
/// <remarks>
/// Ligado na thread da tela, tudo aqui continua nela (os eventos chegam lá). A Twitch só deixa ler os resgates do
/// próprio canal do acesso.
/// </remarks>
public sealed class ResgatesDePontos
{
    public sealed record Resgate(string Nome, string Titulo, int Custo, string TextoDigitado)
    {
        /// <summary>A linha do chat depois do nome: resgatou "Hidratar" (1.500 pontos).</summary>
        public string Texto => $"resgatou \"{Titulo}\" ({Pontos(Custo)})";

        public static string Pontos(int custo) =>
            custo.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + (custo == 1 ? " ponto" : " pontos");
    }

    /// <summary>Uma mensagem do websocket, só com o que o app usa.</summary>
    public sealed record Mensagem(string Tipo, string Id, string? IdDaSessao = null, int? SegundosDeSilencio = null,
        string? EnderecoDeReconexao = null, Resgate? Resgate = null, string? MotivoDaRevogacao = null);

    public const string EnderecoDaTwitch = "wss://eventsub.wss.twitch.tv/ws";
    private const int TentativasDepoisDeCair = 7;
    private static readonly TimeSpan LimiteDasBoasVindas = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FolgaDoSilencio = TimeSpan.FromSeconds(5);

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger<ResgatesDePontos> _log;
    private readonly Uri _endereco;
    private readonly Func<int, TimeSpan> _esperaAntesDaTentativa;

    private CancellationTokenSource? _parar;
    private string _tokenDaAssinatura = string.Empty;

    // As últimas mensagens recebidas: a Twitch pode mandar a mesma duas vezes (e na troca de conexão)
    private readonly Queue<string> _ultimasMensagens = new();

    public ResgatesDePontos(ApiDaTwitch api, ContaDaTwitch conta, ILogger<ResgatesDePontos> log)
        : this(api, conta, log, new Uri(EnderecoDaTwitch), EsperaPadrao) { }

    /// <summary>Os testes usam um servidor local e esperas curtas.</summary>
    public ResgatesDePontos(ApiDaTwitch api, ContaDaTwitch conta, ILogger<ResgatesDePontos> log, Uri endereco,
        Func<int, TimeSpan> esperaAntesDaTentativa)
    {
        _api = api;
        _conta = conta;
        _log = log;
        _endereco = endereco;
        _esperaAntesDaTentativa = esperaAntesDaTentativa;
    }

    /// <summary>Alguém resgatou uma recompensa no canal da conta.</summary>
    public event Action<Resgate>? Resgatado;

    /// <summary><see cref="Problema"/> mudou.</summary>
    public event Action? ProblemaMudou;

    /// <summary>Por que a Twitch recusou os resgates, para a aba Twitch ("" sem problema).</summary>
    public string Problema { get; private set; } = string.Empty;

    public bool EstaLigado => _parar != null;

    /// <summary>
    /// Começa a receber os resgates (se já estiver recebendo com o mesmo acesso, nada muda). Termina quando a
    /// primeira conexão e a assinatura deram certo ou não.
    /// </summary>
    public async Task LigarAsync()
    {
        if (!_conta.EstaConectada || (_parar != null && _tokenDaAssinatura == _conta.Token))
            return;

        Desligar();
        var parar = new CancellationTokenSource();
        _parar = parar;
        _tokenDaAssinatura = _conta.Token;

        var primeira = new TaskCompletionSource();
        _ = RodarAsync(parar, primeira);
        await primeira.Task;
    }

    /// <summary>Para de receber, cancela as tentativas pendentes e esquece o problema.</summary>
    public void Desligar()
    {
        _parar?.Cancel();
        _parar = null;
        _tokenDaAssinatura = string.Empty;
        DefinirProblema(string.Empty);
    }

    /// <summary>A espera antes de cada nova tentativa: cerca de 1, 2, 4, 8, 16, 32 e 60 s.</summary>
    public static TimeSpan EsperaPadrao(int tentativa) =>
        TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, tentativa) + Random.Shared.Next(1000), 60_000));

    private async Task RodarAsync(CancellationTokenSource parar, TaskCompletionSource primeira)
    {
        CancellationToken cancelar = parar.Token;
        try
        {
            // Sem conseguir a primeira conexão, desiste por enquanto (tenta de novo no próximo "Salvar" ou chat carregado)
            Ligacao? atual = await TentarAbrirAsync(_endereco, cancelar);
            while (atual != null)
            {
                bool assinado = await AssinarAsync(atual.IdDaSessao);
                primeira.TrySetResult();
                if (!assinado)
                {
                    atual.Fechar();
                    break;
                }

                if (!await LerAsync(atual, cancelar))
                    break; // a Twitch cancelou a assinatura

                atual = null;
                for (int tentativa = 0; tentativa < TentativasDepoisDeCair && atual == null; tentativa++)
                {
                    await Task.Delay(_esperaAntesDaTentativa(tentativa), cancelar);
                    atual = await TentarAbrirAsync(_endereco, cancelar);
                }
                if (atual == null)
                    _log.LogWarning("Resgates: a conexão caiu e não voltou depois de {Tentativas} tentativas.", TentativasDepoisDeCair);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
        }
        finally
        {
            primeira.TrySetResult();
            if (_parar == parar)
            {
                _parar = null;
                _tokenDaAssinatura = string.Empty;
            }
            parar.Dispose();
        }
    }

    private async Task<bool> AssinarAsync(string idDaSessao)
    {
        ApiDaTwitch.Resposta resposta;
        try
        {
            resposta = await _api.AssinarResgatesAsync(_conta.Token, _conta.Id, idDaSessao);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Resgates: não deu para assinar (sem internet?).");
            return false;
        }

        if (resposta.Codigo == HttpStatusCode.Accepted)
        {
            DefinirProblema(string.Empty);
            return true;
        }

        _log.LogWarning("Resgates: a Twitch recusou a assinatura: {Codigo} {Corpo}", (int)resposta.Codigo, resposta.Corpo);
        if (resposta.Codigo == HttpStatusCode.Unauthorized)
            _conta.AcessoRecusado();
        DefinirProblema(TextoDaRecusa(resposta));
        return false;
    }

    /// <summary>O aviso da aba Twitch quando a Twitch recusa a assinatura dos resgates.</summary>
    public static string TextoDaRecusa(ApiDaTwitch.Resposta resposta)
    {
        string? mensagem = null;
        try { mensagem = (string?)resposta.Json?["message"]; } catch (InvalidOperationException) { }
        string detalhe = string.IsNullOrWhiteSpace(mensagem) ? $"{(int)resposta.Codigo}" : $"{(int)resposta.Codigo}: {mensagem}";

        return resposta.Codigo switch
        {
            HttpStatusCode.Unauthorized => "A conexão com a Twitch expirou. Conecte a conta de novo, acima.",
            HttpStatusCode.Forbidden => $"A Twitch não deixou ler os resgates do seu canal ({detalhe}). Os pontos do canal só existem em canais de afiliados e parceiros; se o seu tem, desconecte e conecte a conta de novo.",
            HttpStatusCode.TooManyRequests => "A Twitch pediu para esperar um pouco. Os resgates voltam quando o chat carregar de novo ou você clicar em Salvar.",
            _ => $"A Twitch recusou mostrar os resgates do seu canal ({detalhe})."
        };
    }

    private void DefinirProblema(string texto)
    {
        if (Problema == texto)
            return;
        Problema = texto;
        ProblemaMudou?.Invoke();
    }

    // Lê até a conexão cair (true: tentar de novo) ou a Twitch cancelar a assinatura (false). Quando a Twitch pede
    // para trocar de endereço, a conexão antiga continua recebendo até a nova dar as boas-vindas.
    private async Task<bool> LerAsync(Ligacao atual, CancellationToken cancelar)
    {
        Task<Ligacao?>? nova = null;
        Task<string?> recebendo = ReceberAsync(atual, cancelar);
        try
        {
            while (true)
            {
                if (nova != null && await Task.WhenAny(recebendo, nova) == nova)
                {
                    Ligacao? trocada = await nova;
                    nova = null;
                    if (trocada != null)
                    {
                        Ignorar(recebendo);
                        atual.Fechar();
                        atual = trocada;
                        recebendo = ReceberAsync(atual, cancelar);
                    }
                    continue;
                }

                string? texto = await recebendo;
                if (texto == null)
                {
                    // Caiu no meio da troca: fica com a nova, se ela vier
                    Ligacao? trocada = nova != null ? await nova : null;
                    nova = null;
                    atual.Fechar();
                    if (trocada == null)
                        return true;
                    atual = trocada;
                    recebendo = ReceberAsync(atual, cancelar);
                    continue;
                }

                Mensagem? mensagem = LerMensagem(texto);
                if (mensagem != null && !JaRecebida(mensagem.Id))
                {
                    switch (mensagem.Tipo)
                    {
                        case "notification" when mensagem.Resgate != null:
                            Resgatado?.Invoke(mensagem.Resgate);
                            break;
                        case "session_reconnect" when nova == null && mensagem.EnderecoDeReconexao != null:
                            nova = TentarAbrirAsync(new Uri(mensagem.EnderecoDeReconexao), cancelar);
                            break;
                        case "revocation":
                            _log.LogWarning("Resgates: a Twitch cancelou a assinatura ({Motivo}).", mensagem.MotivoDaRevogacao);
                            if (mensagem.MotivoDaRevogacao == "authorization_revoked")
                                _conta.AcessoRecusado();
                            atual.Fechar();
                            return false;
                    }
                }
                recebendo = ReceberAsync(atual, cancelar);
            }
        }
        catch (OperationCanceledException)
        {
            atual.Fechar(); // desligado
            throw;
        }
        finally
        {
            if (nova != null)
                _ = nova.ContinueWith(t => t.Result?.Fechar(), TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }

    private bool JaRecebida(string id)
    {
        if (id.Length == 0)
            return false;
        if (_ultimasMensagens.Contains(id))
            return true;
        _ultimasMensagens.Enqueue(id);
        if (_ultimasMensagens.Count > 50)
            _ultimasMensagens.Dequeue();
        return false;
    }

    /// <summary>Lê uma mensagem do websocket; null se não for o formato do EventSub.</summary>
    public static Mensagem? LerMensagem(string texto)
    {
        try
        {
            JsonNode? raiz = JsonNode.Parse(texto);
            string? tipo = (string?)raiz?["metadata"]?["message_type"];
            if (tipo == null)
                return null;
            string id = (string?)raiz?["metadata"]?["message_id"] ?? string.Empty;
            JsonNode? carga = raiz?["payload"];

            switch (tipo)
            {
                case "session_welcome":
                case "session_reconnect":
                    JsonNode? sessao = carga?["session"];
                    return new Mensagem(tipo, id, (string?)sessao?["id"], (int?)sessao?["keepalive_timeout_seconds"],
                        (string?)sessao?["reconnect_url"]);

                case "notification":
                    if ((string?)carga?["subscription"]?["type"] != "channel.channel_points_custom_reward_redemption.add")
                        return new Mensagem(tipo, id);
                    JsonNode? evento = carga?["event"];
                    return new Mensagem(tipo, id, Resgate: new Resgate(
                        (string?)evento?["user_name"] ?? (string?)evento?["user_login"] ?? string.Empty,
                        (string?)evento?["reward"]?["title"] ?? string.Empty,
                        (int?)evento?["reward"]?["cost"] ?? 0,
                        (string?)evento?["user_input"] ?? string.Empty));

                case "revocation":
                    return new Mensagem(tipo, id, MotivoDaRevogacao: (string?)carga?["subscription"]?["status"]);

                default:
                    return new Mensagem(tipo, id);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // Uma conexão já com as boas-vindas; null se não deu (sem internet, Twitch fora do ar...)
    private async Task<Ligacao?> TentarAbrirAsync(Uri endereco, CancellationToken cancelar)
    {
        var ws = new ClientWebSocket();
        try
        {
            using (var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelar))
            {
                limite.CancelAfter(TimeSpan.FromSeconds(15));
                await ws.ConnectAsync(endereco, limite.Token);
            }

            var ligacao = new Ligacao(ws, LimiteDasBoasVindas);
            string? texto = await ReceberAsync(ligacao, cancelar);
            Mensagem? boasVindas = texto != null ? LerMensagem(texto) : null;
            if (boasVindas?.Tipo != "session_welcome" || string.IsNullOrEmpty(boasVindas.IdDaSessao))
                throw new WebSocketException("A Twitch não deu as boas-vindas.");

            ligacao.IdDaSessao = boasVindas.IdDaSessao;
            ligacao.Silencio = TimeSpan.FromSeconds(boasVindas.SegundosDeSilencio ?? 10) + FolgaDoSilencio;
            JaRecebida(boasVindas.Id);
            return ligacao;
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
            ws.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Resgates: não deu para conectar ao EventSub.");
            ws.Dispose();
            return null;
        }
    }

    // Uma mensagem inteira; null quando a conexão fechou, deu erro ou ficou em silêncio além do combinado
    private static async Task<string?> ReceberAsync(Ligacao ligacao, CancellationToken cancelar)
    {
        using var silencio = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
        silencio.CancelAfter(ligacao.Silencio);
        var buffer = new byte[8 * 1024];
        using var mensagem = new MemoryStream();
        try
        {
            while (true)
            {
                WebSocketReceiveResult parte = await ligacao.Ws.ReceiveAsync(buffer, silencio.Token);
                if (parte.MessageType == WebSocketMessageType.Close)
                    return null;
                mensagem.Write(buffer, 0, parte.Count);
                if (parte.EndOfMessage)
                    return Encoding.UTF8.GetString(mensagem.GetBuffer(), 0, (int)mensagem.Length);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            return null;
        }
    }

    private static void Ignorar(Task tarefa) => _ = tarefa.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

    private sealed class Ligacao(ClientWebSocket ws, TimeSpan silencio)
    {
        public ClientWebSocket Ws { get; } = ws;
        public string IdDaSessao { get; set; } = string.Empty;
        public TimeSpan Silencio { get; set; } = silencio;

        public void Fechar()
        {
            try { Ws.Abort(); } catch (Exception) { }
            Ws.Dispose();
        }
    }
}
