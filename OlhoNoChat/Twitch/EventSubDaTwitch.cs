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
/// Os eventos da Twitch que a conta conectada recebe, pelo EventSub num websocket: os resgates de pontos do canal da conta
/// e as mensagens que o AutoMod segurou no canal aberto (quando a conta é dona ou moderadora dele). A Twitch dá
/// boas-vindas com o id da sessão, o app assina os eventos nessa sessão e a Twitch manda cada um, com um "estou vivo"
/// nos intervalos. Se a conexão cai, tenta de novo até 7 vezes.
/// </summary>
/// <remarks>
/// Uma conexão só para tudo: a Twitch deixa cada conta ter no máximo 3 conexões com assinaturas (o app instalado e o de
/// desenvolvimento abertos juntos já usam duas). Pedir outra coisa (outro canal, um evento a mais ou a menos) abre uma
/// sessão nova com as assinaturas novas. Ligado na thread da tela, tudo aqui continua nela (os eventos chegam lá).
/// </remarks>
public sealed class EventSubDaTwitch
{
    public sealed record Resgate(string Nome, string Titulo, int Custo, string TextoDigitado)
    {
        /// <summary>A linha do chat depois do nome: resgatou "Hidratar" (1.500 pontos).</summary>
        public string Texto => $"resgatou \"{Titulo}\" ({Pontos(Custo)})";

        public static string Pontos(int custo) =>
            custo.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + (custo == 1 ? " ponto" : " pontos");
    }

    /// <summary>Um pedaço da mensagem segurada: texto, ou um emote da Twitch (com o id dele).</summary>
    public sealed record ParteDaMensagem(string Texto, string? IdDoEmote = null);

    /// <summary>
    /// Uma mensagem que o AutoMod segurou para um moderador decidir. <paramref name="Motivo"/>: "automod", "blocked_term"
    /// ou "blocked_link"; <paramref name="Categoria"/> só com "automod" (como "swearing").
    /// </summary>
    public sealed record MensagemRetida(string Id, string Login, string Nome, IReadOnlyList<ParteDaMensagem> Partes,
        string Motivo, string? Categoria)
    {
        public string Texto => string.Concat(Partes.Select(p => p.Texto));

        // Os records comparam listas pela referência: duas mensagens iguais têm partes iguais
        public bool Equals(MensagemRetida? outra) =>
            outra is not null && Id == outra.Id && Login == outra.Login && Nome == outra.Nome && Motivo == outra.Motivo
            && Categoria == outra.Categoria && Partes.SequenceEqual(outra.Partes);

        public override int GetHashCode() => HashCode.Combine(Id, Login, Motivo);
    }

    /// <summary>
    /// Uma mensagem do AutoMod saiu da fila: aprovada, recusada (por qualquer moderador) ou expirada. O status vem em
    /// minúsculas: "approved", "denied" ou "expired".
    /// </summary>
    public sealed record Decisao(string Id, string Status);

    /// <summary>
    /// Um anúncio começou no canal da conta (pela Ads Manager, sozinho, ou pedido por alguém): quando e por quantos
    /// segundos.
    /// </summary>
    public sealed record AnuncioComecou(DateTimeOffset Inicio, int Segundos, bool Automatico);

    /// <summary>Como está o AutoMod do canal pedido, para a aba Twitch.</summary>
    public enum SituacaoDoAutoMod
    {
        /// <summary>Não foi pedido, ou a conexão está desligada.</summary>
        Desligado,
        /// <summary>A Twitch aceitou: as mensagens seguradas chegam.</summary>
        Funcionando,
        /// <summary>A conta não é dona nem moderadora do canal (a Twitch recusou com 403, ou cancelou depois).</summary>
        NaoModera,
        /// <summary>Sem internet ou outra recusa da Twitch.</summary>
        Falhou,
    }

    /// <summary>
    /// O que receber: os resgates do canal da conta, o AutoMod do canal com esse id (null = nenhum) e o começo dos
    /// anúncios do canal da conta.
    /// </summary>
    public sealed record Pedido(bool Resgates, string? CanalDoAutoMod, bool Anuncios = false)
    {
        public bool Vazio => !Resgates && CanalDoAutoMod == null && !Anuncios;
    }

    /// <summary>Uma mensagem do websocket, só com o que o app usa.</summary>
    public sealed record Mensagem(string Tipo, string Id, string? IdDaSessao = null, int? SegundosDeSilencio = null,
        string? EnderecoDeReconexao = null, string? TipoDaAssinatura = null, Resgate? Resgate = null,
        MensagemRetida? Retida = null, Decisao? Decidida = null, string? MotivoDaRevogacao = null,
        AnuncioComecou? Anuncio = null);

    public const string EnderecoDaTwitch = "wss://eventsub.wss.twitch.tv/ws";
    public const string TipoDosResgates = "channel.channel_points_custom_reward_redemption.add";
    public const string TipoDaRetida = "automod.message.hold";
    public const string TipoDaDecisao = "automod.message.update";
    public const string TipoDoAnuncio = "channel.ad_break.begin";
    private const int TentativasDepoisDeCair = 7;
    private static readonly TimeSpan LimiteDasBoasVindas = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FolgaDoSilencio = TimeSpan.FromSeconds(5);

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger<EventSubDaTwitch> _log;
    private readonly Uri _endereco;
    private readonly Func<int, TimeSpan> _esperaAntesDaTentativa;

    private CancellationTokenSource? _parar;
    private string _tokenDaAssinatura = string.Empty;
    private Pedido _pedido = new(false, null);

    // As últimas mensagens recebidas: a Twitch pode mandar a mesma duas vezes (e na troca de conexão)
    private readonly Queue<string> _ultimasMensagens = new();

    public EventSubDaTwitch(ApiDaTwitch api, ContaDaTwitch conta, ILogger<EventSubDaTwitch> log)
        : this(api, conta, log, new Uri(EnderecoDaTwitch), EsperaPadrao) { }

    /// <summary>Os testes usam um servidor local e esperas curtas.</summary>
    public EventSubDaTwitch(ApiDaTwitch api, ContaDaTwitch conta, ILogger<EventSubDaTwitch> log, Uri endereco,
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

    /// <summary>O AutoMod segurou uma mensagem no canal pedido.</summary>
    public event Action<MensagemRetida>? MensagemFoiRetida;

    /// <summary>Uma mensagem do AutoMod foi aprovada, recusada ou expirou (aqui, no site da Twitch ou por outro moderador).</summary>
    public event Action<Decisao>? MensagemDecidida;

    /// <summary>Um anúncio começou no canal da conta.</summary>
    public event Action<AnuncioComecou>? AnuncioComecouNoCanal;

    /// <summary><see cref="Problema"/> ou <see cref="AutoMod"/> mudou (em qualquer thread).</summary>
    public event Action? ProblemaMudou;

    /// <summary>Por que a Twitch recusou os resgates, para a aba Twitch ("" sem problema).</summary>
    public string Problema { get; private set; } = string.Empty;

    /// <summary>Se as mensagens seguradas pelo AutoMod do canal pedido estão chegando.</summary>
    public SituacaoDoAutoMod AutoMod { get; private set; }

    public bool EstaLigado => _parar != null;

    /// <summary>
    /// Começa a receber o que foi pedido (se já estiver recebendo isso com o mesmo acesso, nada muda; um pedido vazio
    /// desliga). Termina quando a primeira conexão e as assinaturas deram certo ou não.
    /// </summary>
    public async Task LigarAsync(Pedido pedido)
    {
        if (!_conta.EstaConectada || pedido.Vazio)
        {
            Desligar();
            return;
        }
        if (_parar != null && _tokenDaAssinatura == _conta.Token && _pedido == pedido)
            return;

        Desligar();
        var parar = new CancellationTokenSource();
        _parar = parar;
        _tokenDaAssinatura = _conta.Token;
        _pedido = pedido;

        var primeira = new TaskCompletionSource();
        _ = RodarAsync(parar, pedido, primeira);
        await primeira.Task;
    }

    /// <summary>Para de receber, cancela as tentativas pendentes e esquece o problema.</summary>
    public void Desligar()
    {
        _parar?.Cancel();
        _parar = null;
        _tokenDaAssinatura = string.Empty;
        _pedido = new(false, null);
        DefinirProblema(string.Empty);
        DefinirAutoMod(SituacaoDoAutoMod.Desligado);
    }

    /// <summary>A espera antes de cada nova tentativa: cerca de 1, 2, 4, 8, 16, 32 e 60 s.</summary>
    public static TimeSpan EsperaPadrao(int tentativa) =>
        TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, tentativa) + Random.Shared.Next(1000), 60_000));

    private async Task RodarAsync(CancellationTokenSource parar, Pedido pedido, TaskCompletionSource primeira)
    {
        CancellationToken cancelar = parar.Token;
        try
        {
            // Sem conseguir a primeira conexão, desiste por enquanto (tenta de novo no próximo "Salvar" ou chat carregado)
            Ligacao? atual = await TentarAbrirAsync(_endereco, cancelar);
            while (atual != null)
            {
                HashSet<string> assinadas = await AssinarAsync(pedido, atual.IdDaSessao);
                primeira.TrySetResult();
                if (assinadas.Count == 0)
                {
                    atual.Fechar();
                    break;
                }

                if (!await LerAsync(atual, assinadas, cancelar))
                    break; // a Twitch cancelou as assinaturas

                atual = null;
                for (int tentativa = 0; tentativa < TentativasDepoisDeCair && atual == null; tentativa++)
                {
                    await Task.Delay(_esperaAntesDaTentativa(tentativa), cancelar);
                    atual = await TentarAbrirAsync(_endereco, cancelar);
                }
                if (atual == null)
                    _log.LogWarning("EventSub: a conexão caiu e não voltou depois de {Tentativas} tentativas.", TentativasDepoisDeCair);
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
                _pedido = new(false, null);
                DefinirAutoMod(SituacaoDoAutoMod.Desligado);
            }
            parar.Dispose();
        }
    }

    // Assina o que foi pedido na sessão; devolve os tipos que a Twitch aceitou (nenhum: a conexão não serve para nada)
    private async Task<HashSet<string>> AssinarAsync(Pedido pedido, string idDaSessao)
    {
        var assinadas = new HashSet<string>();
        if (pedido.Resgates)
        {
            ApiDaTwitch.Resposta? resposta = await AssinarUmAsync(TipoDosResgates, "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = _conta.Id }, idDaSessao);
            if (resposta?.Codigo == HttpStatusCode.Accepted)
            {
                assinadas.Add(TipoDosResgates);
                DefinirProblema(string.Empty);
            }
            else if (resposta != null)
            {
                DefinirProblema(TextoDaRecusa(resposta));
            }
        }

        if (pedido.CanalDoAutoMod is string canal)
        {
            var condicao = new Dictionary<string, string> { ["broadcaster_user_id"] = canal, ["moderator_user_id"] = _conta.Id };
            SituacaoDoAutoMod situacao = SituacaoDoAutoMod.Funcionando;
            foreach (string tipo in new[] { TipoDaRetida, TipoDaDecisao })
            {
                ApiDaTwitch.Resposta? resposta = await AssinarUmAsync(tipo, "2", condicao, idDaSessao);
                if (resposta?.Codigo != HttpStatusCode.Accepted)
                {
                    // 403: a conta não é dona nem moderadora do canal aberto, o caso mais comum (nada aparece)
                    situacao = resposta?.Codigo == HttpStatusCode.Forbidden ? SituacaoDoAutoMod.NaoModera : SituacaoDoAutoMod.Falhou;
                    if (situacao == SituacaoDoAutoMod.NaoModera)
                        _log.LogInformation("EventSub: a conta não modera o canal aberto; sem AutoMod.");
                    break;
                }
                assinadas.Add(tipo);
            }
            DefinirAutoMod(situacao);
        }

        // Só o dono do canal: a falta (canal sem anúncios, conta sem a permissão) só faz o aviso de "começou" não vir; a
        // consulta do próximo anúncio continua (Twitch/AnunciosDoCanal.cs)
        if (pedido.Anuncios)
        {
            ApiDaTwitch.Resposta? resposta = await AssinarUmAsync(TipoDoAnuncio, "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = _conta.Id }, idDaSessao);
            if (resposta?.Codigo == HttpStatusCode.Accepted)
                assinadas.Add(TipoDoAnuncio);
        }
        return assinadas;
    }

    // A resposta da Twitch, ou null sem internet
    private async Task<ApiDaTwitch.Resposta?> AssinarUmAsync(string tipo, string versao, Dictionary<string, string> condicao,
        string idDaSessao)
    {
        ApiDaTwitch.Resposta resposta;
        try
        {
            resposta = await _api.AssinarAsync(_conta.Token, tipo, versao, condicao, idDaSessao);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EventSub: não deu para assinar {Tipo} (sem internet?).", tipo);
            return null;
        }

        if (resposta.Codigo != HttpStatusCode.Accepted && resposta.Codigo != HttpStatusCode.Forbidden)
            _log.LogWarning("EventSub: a Twitch recusou {Tipo}: {Codigo} {Corpo}", tipo, (int)resposta.Codigo, resposta.Corpo);
        if (resposta.Codigo == HttpStatusCode.Unauthorized)
            _conta.AcessoRecusado();
        return resposta;
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

    private void DefinirAutoMod(SituacaoDoAutoMod situacao)
    {
        if (AutoMod == situacao)
            return;
        AutoMod = situacao;
        ProblemaMudou?.Invoke();
    }

    private void DefinirProblema(string texto)
    {
        if (Problema == texto)
            return;
        Problema = texto;
        ProblemaMudou?.Invoke();
    }

    // Lê até a conexão cair (true: tentar de novo) ou a Twitch cancelar todas as assinaturas (false). Quando a Twitch
    // pede para trocar de endereço, a conexão antiga continua recebendo até a nova dar as boas-vindas.
    private async Task<bool> LerAsync(Ligacao atual, HashSet<string> assinadas, CancellationToken cancelar)
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
                        case "notification" when mensagem.Retida != null:
                            MensagemFoiRetida?.Invoke(mensagem.Retida);
                            break;
                        case "notification" when mensagem.Decidida != null:
                            MensagemDecidida?.Invoke(mensagem.Decidida);
                            break;
                        case "notification" when mensagem.Anuncio != null:
                            AnuncioComecouNoCanal?.Invoke(mensagem.Anuncio);
                            break;
                        case "session_reconnect" when nova == null && mensagem.EnderecoDeReconexao != null:
                            nova = TentarAbrirAsync(new Uri(mensagem.EnderecoDeReconexao), cancelar);
                            break;
                        case "revocation":
                            _log.LogWarning("EventSub: a Twitch cancelou {Tipo} ({Motivo}).", mensagem.TipoDaAssinatura, mensagem.MotivoDaRevogacao);
                            if (mensagem.MotivoDaRevogacao == "authorization_revoked")
                            {
                                _conta.AcessoRecusado();
                                assinadas.Clear();
                            }
                            // Sem a conta como moderadora, as duas do AutoMod vão juntas
                            else if (mensagem.TipoDaAssinatura is TipoDaRetida or TipoDaDecisao)
                            {
                                assinadas.Remove(TipoDaRetida);
                                assinadas.Remove(TipoDaDecisao);
                                DefinirAutoMod(SituacaoDoAutoMod.NaoModera);
                            }
                            else if (mensagem.TipoDaAssinatura != null)
                            {
                                assinadas.Remove(mensagem.TipoDaAssinatura);
                            }
                            if (assinadas.Count == 0)
                            {
                                atual.Fechar();
                                return false;
                            }
                            break;
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
            string? assinatura = (string?)carga?["subscription"]?["type"];

            switch (tipo)
            {
                case "session_welcome":
                case "session_reconnect":
                    JsonNode? sessao = carga?["session"];
                    return new Mensagem(tipo, id, (string?)sessao?["id"], (int?)sessao?["keepalive_timeout_seconds"],
                        (string?)sessao?["reconnect_url"]);

                case "notification":
                    JsonNode? evento = carga?["event"];
                    return assinatura switch
                    {
                        TipoDosResgates => new Mensagem(tipo, id, TipoDaAssinatura: assinatura, Resgate: new Resgate(
                            (string?)evento?["user_name"] ?? (string?)evento?["user_login"] ?? string.Empty,
                            (string?)evento?["reward"]?["title"] ?? string.Empty,
                            (int?)evento?["reward"]?["cost"] ?? 0,
                            (string?)evento?["user_input"] ?? string.Empty)),
                        TipoDaRetida => new Mensagem(tipo, id, TipoDaAssinatura: assinatura, Retida: LerRetida(evento)),
                        TipoDoAnuncio => new Mensagem(tipo, id, TipoDaAssinatura: assinatura, Anuncio: LerAnuncio(evento)),
                        TipoDaDecisao when (string?)evento?["message_id"] is { Length: > 0 } idDaMensagem =>
                            new Mensagem(tipo, id, TipoDaAssinatura: assinatura,
                                Decidida: new Decisao(idDaMensagem, ((string?)evento?["status"] ?? string.Empty).ToLowerInvariant())),
                        _ => new Mensagem(tipo, id, TipoDaAssinatura: assinatura),
                    };

                case "revocation":
                    return new Mensagem(tipo, id, TipoDaAssinatura: assinatura, MotivoDaRevogacao: (string?)carga?["subscription"]?["status"]);

                default:
                    return new Mensagem(tipo, id);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // channel.ad_break.begin: o exemplo da Twitch traz a duração e o "automático" entre aspas, a tabela como número e
    // booleano; aceita os dois. Sem o horário, conta de agora
    private static AnuncioComecou? LerAnuncio(JsonNode? evento)
    {
        if (evento == null)
            return null;
        int segundos = NumeroOuTexto(evento["duration_seconds"]);
        if (segundos <= 0)
            return null;
        DateTimeOffset inicio = DateTimeOffset.TryParse((string?)evento["started_at"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTimeOffset lido) ? lido : DateTimeOffset.UtcNow;
        JsonNode? automatico = evento["is_automatic"];
        bool ehAutomatico = automatico?.GetValueKind() == JsonValueKind.True
                            || (automatico?.GetValueKind() == JsonValueKind.String && (string?)automatico == "true");
        return new AnuncioComecou(inicio, segundos, ehAutomatico);
    }

    /// <summary>Um número que pode vir como número ou como texto (0 se não for nenhum dos dois).</summary>
    public static int NumeroOuTexto(JsonNode? valor) => valor?.GetValueKind() switch
    {
        JsonValueKind.Number => (int)Math.Clamp(valor.GetValue<double>(), int.MinValue, int.MaxValue),
        JsonValueKind.String when long.TryParse((string?)valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) =>
            (int)Math.Clamp(n, int.MinValue, int.MaxValue),
        _ => 0,
    };

    // automod.message.hold versão 2; null sem o id da mensagem (não daria para decidir)
    private static MensagemRetida? LerRetida(JsonNode? evento)
    {
        if ((string?)evento?["message_id"] is not { Length: > 0 } id)
            return null;
        JsonNode? mensagem = evento["message"];
        var partes = new List<ParteDaMensagem>();
        foreach (JsonNode? parte in mensagem?["fragments"] as JsonArray ?? [])
        {
            string textoDaParte = (string?)parte?["text"] ?? string.Empty;
            if (textoDaParte.Length == 0)
                continue;
            string? emote = (string?)parte?["type"] == "emote" ? (string?)parte?["emote"]?["id"] : null;
            partes.Add(new ParteDaMensagem(textoDaParte, string.IsNullOrEmpty(emote) ? null : emote));
        }
        // Sem os pedaços, o texto inteiro
        if (partes.Count == 0 && (string?)mensagem?["text"] is { Length: > 0 } inteiro)
            partes.Add(new ParteDaMensagem(inteiro));

        string login = (string?)evento["user_login"] ?? string.Empty;
        return new MensagemRetida(id, login, (string?)evento["user_name"] ?? login, partes,
            (string?)evento["reason"] ?? "automod", (string?)evento["automod"]?["category"]);
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
            _log.LogWarning(ex, "EventSub: não deu para conectar.");
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
