using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Overlay;

/// <summary>Como o overlay da live está.</summary>
public enum EstadoDoOverlay
{
    /// <summary>A opção está desligada: nenhuma porta aberta.</summary>
    Desligado,

    /// <summary>O endereço está aberto e o OBS pode usar.</summary>
    Funcionando,

    /// <summary>Outro programa usa a porta: o app tenta de novo de tempos em tempos.</summary>
    PortaOcupada,

    /// <summary>Outro Olho no Chat aberto (uma segunda cópia, a versão de teste) já usa a porta.</summary>
    OutroOlhoNoChat,
}

/// <summary>
/// O overlay da live: um servidor só do próprio PC (<c>http://localhost:&lt;porta&gt;/overlay</c>) com a página para a
/// Fonte de navegador do OBS (browser/overlay.html) e um fluxo de eventos (Server-Sent Events) com as mensagens que o chat
/// Padrão mostrou. O endereço é fixo: a porta muda só quando a pessoa pede (<see cref="EncontrarPortaLivre"/>).
/// </summary>
/// <remarks>
/// Os eventos: <c>config</c> (as opções, ao conectar e a cada Salvar), <c>line</c> (o HTML de uma linha),
/// <c>remove</c> (um seletor das linhas que saem), <c>replace</c> (uma linha que mudou) e <c>clear</c>. É só tempo real
/// (decisão do usuário, 2026-11-06): nada fica guardado, e quem conecta (o OBS aberto, a fonte recarregada) vê só o que
/// chegar dali em diante. Ao conectar, vai também <c>version</c>: uma marca dos arquivos da página. O OBS mantém a página
/// carregada; se o app abrir com outra versão dela (uma atualização), a página se recarrega sozinha.
/// </remarks>
public sealed class ServidorDoOverlay : IDisposable
{
#if DEBUG
    // A versão de desenvolvimento tem a sua, para rodar ao lado do app instalado (como a pasta de dados)
    public const int PortaPadrao = 8988;
#else
    public const int PortaPadrao = 8987;
#endif

    /// <summary>
    /// As portas que "Encontrar outro endereço" tenta, na ordem. Ficam fora das faixas que o Windows usa para conexões
    /// temporárias (49152 em diante) e longe das do "Conectar" da Twitch (8981...).
    /// </summary>
    public static readonly IReadOnlyList<int> PortasParaTentar = [PortaPadrao, 8989, 8990, 18987, 28987, 38987, 45987];

    /// <summary>O que <c>/overlay/quem</c> responde: é assim que outro Olho no Chat na porta é reconhecido.</summary>
    public const string Marca = "olho-no-chat-overlay";

    // Os arquivos que a página do overlay usa, da pasta "browser" (o chat.css desenha as linhas do mesmo jeito do chat)
    private static readonly Dictionary<string, (string Arquivo, string Tipo)> Arquivos = new()
    {
        ["/overlay"] = ("overlay.html", "text/html; charset=utf-8"),
        ["/overlay/"] = ("overlay.html", "text/html; charset=utf-8"),
        ["/overlay.js"] = ("overlay.js", "text/javascript; charset=utf-8"),
        ["/overlay.css"] = ("overlay.css", "text/css; charset=utf-8"),
        ["/chat.css"] = ("chat.css", "text/css; charset=utf-8"),
    };

    private readonly ILogger _log;
    private readonly string _pasta;
    private readonly TimeSpan _esperaParaTentarDeNovo;
    private readonly TimeSpan _intervaloDoPing;
    private readonly object _trava = new();
    private readonly List<Cliente> _clientes = [];
    private CancellationTokenSource? _rodando;
    private string _opcoes = "{}";
    private string? _versaoDasPaginas;

    public ServidorDoOverlay(ILogger<ServidorDoOverlay> log)
        : this(log, InfoDoApp.PastaDasPaginas, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15))
    {
    }

    /// <summary>Os testes trocam a pasta das páginas e as esperas.</summary>
    public ServidorDoOverlay(ILogger log, string pastaDasPaginas, TimeSpan esperaParaTentarDeNovo, TimeSpan intervaloDoPing)
    {
        _log = log;
        _pasta = pastaDasPaginas;
        _esperaParaTentarDeNovo = esperaParaTentarDeNovo;
        _intervaloDoPing = intervaloDoPing;
    }

    /// <summary>Mudou de estado (em outra thread).</summary>
    public event Action<EstadoDoOverlay>? EstadoMudou;

    /// <summary>Uma Fonte de navegador conectou ou saiu (em outra thread): <see cref="Conectados"/> mudou.</summary>
    public event Action? ConectadosMudaram;

    public EstadoDoOverlay Estado { get; private set; } = EstadoDoOverlay.Desligado;

    /// <summary>A porta pedida no último <see cref="Ligar"/> (0 antes).</summary>
    public int Porta { get; private set; }

    /// <summary>Quantas Fontes de navegador estão conectadas agora.</summary>
    public int Conectados
    {
        get
        {
            lock (_trava)
                return _clientes.Count;
        }
    }

    public static string EnderecoDa(int porta) => $"http://localhost:{porta}/overlay";

    public static bool PortaValida(int porta) => porta is >= 1024 and <= 65535;

    /// <summary>
    /// Abre o endereço na porta (ou continua aberto, se já é ela) com as opções em JSON
    /// (<see cref="ContratoDoOverlay.JsonDasOpcoes"/>). Porta ocupada: tenta de novo de tempos em tempos.
    /// </summary>
    public void Ligar(int porta, string opcoes)
    {
        lock (_trava)
        {
            _opcoes = opcoes;
            if (_rodando != null && Porta == porta)
            {
                Transmitir("config", _opcoes);
                return;
            }
        }
        Parar();
        CancellationTokenSource rodando;
        lock (_trava)
        {
            Porta = porta;
            rodando = new CancellationTokenSource();
            _rodando = rodando;
        }
        _ = Task.Run(() => RodarAsync(porta, rodando));
    }

    /// <summary>Fecha o endereço (a opção foi desligada, o app vai fechar).</summary>
    public void Desligar()
    {
        Parar();
        Mudar(EstadoDoOverlay.Desligado, null);
    }

    public void Dispose() => Desligar();

    /// <summary>As opções novas (Salvar): o overlay aplica na hora, sem perder as mensagens da tela.</summary>
    public void MudarOpcoes(string opcoes)
    {
        lock (_trava)
        {
            _opcoes = opcoes;
            Transmitir("config", _opcoes);
        }
    }

    /// <summary>Uma linha que o chat Padrão mostrou (o HTML pronto).</summary>
    public void EnviarLinha(string html) => Enviar("line", new { html });

    /// <summary>As linhas que saíram do chat (mensagem apagada, banimento, um aviso desligado): o seletor CSS delas.</summary>
    public void EnviarRemocao(string seletor) => Enviar("remove", new { selector = seletor });

    /// <summary>Uma linha já mandada mudou (os selos ou a foto do chat unido chegaram depois): o seletor dela e o HTML novo.</summary>
    public void EnviarSubstituicao(string seletor, string html) => Enviar("replace", new { selector = seletor, html });

    /// <summary>Tira tudo do overlay (o atalho, o botão da aba, o /clear de um moderador, outro canal).</summary>
    public void Limpar() => Enviar("clear", new { });

    /// <summary>Uma linha de exemplo ("Mensagens de teste"), só para ver o visual: não espera o atraso.</summary>
    public void EnviarTeste(string html) => Enviar("line", new { html, test = true });

    /// <summary>A porta está livre para o overlay agora (abre e fecha na hora).</summary>
    public static bool PortaLivre(int porta)
    {
        var teste = new HttpListener();
        teste.Prefixes.Add($"http://localhost:{porta}/");
        try
        {
            teste.Start();
            return true;
        }
        catch (HttpListenerException)
        {
            return false;
        }
        finally
        {
            teste.Close();
        }
    }

    /// <summary>
    /// "Encontrar outro endereço": a primeira das <see cref="PortasParaTentar"/> (menos a de agora) que abre de verdade;
    /// null se nenhuma abrir.
    /// </summary>
    public static int? EncontrarPortaLivre(int atual, Func<int, bool>? livre = null)
    {
        livre ??= PortaLivre;
        foreach (int porta in PortasParaTentar)
        {
            if (porta != atual && livre(porta))
                return porta;
        }
        return null;
    }

    // --- Por dentro --------------------------------------------------------------------------------------------

    private sealed class Cliente(HttpListenerResponse resposta)
    {
        public HttpListenerResponse Resposta { get; } = resposta;

        // Um cliente travado não segura os outros: o mais velho sai quando enche
        public Channel<string> Fila { get; } = Channel.CreateBounded<string>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    }

    private void Parar()
    {
        CancellationTokenSource? rodando;
        List<Cliente> clientes;
        lock (_trava)
        {
            rodando = _rodando;
            _rodando = null;
            clientes = [.. _clientes];
            _clientes.Clear();
        }
        rodando?.Cancel();
        foreach (Cliente cliente in clientes)
            cliente.Fila.Writer.TryComplete();
        if (clientes.Count > 0)
            ConectadosMudaram?.Invoke();
    }

    private void Enviar(string tipo, object dados)
    {
        string json = JsonSerializer.Serialize(dados);
        lock (_trava)
            Transmitir(tipo, json);
    }

    // Dentro da trava
    private void Transmitir(string tipo, string json)
    {
        string texto = Evento(tipo, json);
        foreach (Cliente cliente in _clientes)
            cliente.Fila.Writer.TryWrite(texto);
    }

    private static string Evento(string tipo, string json) => $"event: {tipo}\ndata: {json}\n\n";

    private async Task RodarAsync(int porta, CancellationTokenSource rodando)
    {
        CancellationToken cancelar = rodando.Token;
        while (!cancelar.IsCancellationRequested)
        {
            var servidor = new HttpListener();
            servidor.Prefixes.Add($"http://localhost:{porta}/");
            try
            {
                servidor.Start();
            }
            catch (HttpListenerException ex)
            {
                servidor.Close();
                bool nosso = await EhOutroOlhoNoChatAsync(porta, cancelar).ConfigureAwait(false);
                _log.LogInformation("Overlay: porta {Porta} ocupada ({Quem}): {Erro}", porta, nosso ? "outro Olho no Chat" : "outro programa",
                    ex.Message);
                Mudar(nosso ? EstadoDoOverlay.OutroOlhoNoChat : EstadoDoOverlay.PortaOcupada, rodando);
                try
                {
                    await Task.Delay(_esperaParaTentarDeNovo, cancelar).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            _log.LogInformation("Overlay: aberto em {Endereco}.", EnderecoDa(porta));
            Mudar(EstadoDoOverlay.Funcionando, rodando);
            await AtenderAsync(servidor, cancelar).ConfigureAwait(false);
            return;
        }
    }

    // Quem está na porta responde a /overlay/quem com a marca? (uma segunda cópia do app, a versão de teste)
    private static async Task<bool> EhOutroOlhoNoChatAsync(int porta, CancellationToken cancelar)
    {
        try
        {
            using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string resposta = await cliente.GetStringAsync($"http://localhost:{porta}/overlay/quem", cancelar).ConfigureAwait(false);
            return resposta.Contains(Marca, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task AtenderAsync(HttpListener servidor, CancellationToken cancelar)
    {
        using CancellationTokenRegistration aoCancelar = cancelar.Register(() =>
        {
            try
            {
                servidor.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        using var ping = new PeriodicTimer(_intervaloDoPing);
        _ = PingarAsync(ping, cancelar);
        while (!cancelar.IsCancellationRequested)
        {
            HttpListenerContext pedido;
            try
            {
                pedido = await servidor.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (cancelar.IsCancellationRequested || ex is ObjectDisposedException)
                    break;
                // Um pedido que deu errado no meio (o OBS fechou a conexão) não fecha o endereço
                _log.LogWarning(ex, "Overlay: um pedido falhou; o endereço continua aberto.");
                continue;
            }
            _ = Task.Run(() => ResponderAsync(pedido, cancelar), CancellationToken.None);
        }
        servidor.Close();
    }

    // Um comentário de tempos em tempos: uma Fonte que sumiu (OBS fechado) é percebida quando a escrita falha
    private async Task PingarAsync(PeriodicTimer ping, CancellationToken cancelar)
    {
        try
        {
            while (await ping.WaitForNextTickAsync(cancelar).ConfigureAwait(false))
            {
                lock (_trava)
                {
                    foreach (Cliente cliente in _clientes)
                        cliente.Fila.Writer.TryWrite(": ping\n\n");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ResponderAsync(HttpListenerContext pedido, CancellationToken cancelar)
    {
        HttpListenerResponse resposta = pedido.Response;
        string caminho = pedido.Request.Url?.AbsolutePath ?? string.Empty;
        try
        {
            if (pedido.Request.HttpMethod != "GET")
                Responder(resposta, 405, "text/plain; charset=utf-8", string.Empty);
            else if (caminho == "/overlay/eventos")
                await TransmitirParaAsync(resposta, cancelar).ConfigureAwait(false);
            else if (caminho == "/overlay/quem")
                Responder(resposta, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new { app = Marca }));            else if (Arquivos.TryGetValue(caminho, out var arquivo) && File.Exists(Path.Combine(_pasta, arquivo.Arquivo)))
                Responder(resposta, 200, arquivo.Tipo, await File.ReadAllTextAsync(Path.Combine(_pasta, arquivo.Arquivo), cancelar).ConfigureAwait(false));
            else
                Responder(resposta, 404, "text/plain; charset=utf-8", string.Empty);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException
                                       or IOException or OperationCanceledException)
        {
            // O OBS fechou a fonte, ou o servidor fechou
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Overlay: erro ao responder {Caminho}.", caminho);
        }
        finally
        {
            try
            {
                resposta.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private static void Responder(HttpListenerResponse resposta, int codigo, string tipo, string corpo)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(corpo);
        resposta.StatusCode = codigo;
        resposta.ContentType = tipo;
        resposta.Headers["Cache-Control"] = "no-store";
        resposta.ContentLength64 = bytes.Length;
        resposta.OutputStream.Write(bytes);
    }

    // O fluxo de eventos de uma Fonte de navegador: as opções e depois cada evento novo
    private async Task TransmitirParaAsync(HttpListenerResponse resposta, CancellationToken cancelar)
    {
        resposta.StatusCode = 200;
        resposta.ContentType = "text/event-stream; charset=utf-8";
        resposta.Headers["Cache-Control"] = "no-store";
        resposta.SendChunked = true;

        var cliente = new Cliente(resposta);
        var inicio = new StringBuilder("retry: 2000\n\n");
        lock (_trava)
        {
            if (cancelar.IsCancellationRequested)
                return;
            inicio.Append(Evento("version", JsonSerializer.Serialize(new { v = VersaoDasPaginas() })));
            inicio.Append(Evento("config", _opcoes));
            _clientes.Add(cliente);
        }
        ConectadosMudaram?.Invoke();

        Stream saida = resposta.OutputStream;
        try
        {
            await EscreverAsync(saida, inicio.ToString(), cancelar).ConfigureAwait(false);
            await foreach (string texto in cliente.Fila.Reader.ReadAllAsync(cancelar).ConfigureAwait(false))
                await EscreverAsync(saida, texto, cancelar).ConfigureAwait(false);
        }
        finally
        {
            lock (_trava)
                _clientes.Remove(cliente);
            ConectadosMudaram?.Invoke();
        }
    }

    /// <summary>Uma marca do conteúdo dos arquivos da página (muda quando algum deles muda).</summary>
    public string VersaoDasPaginas()
    {
        if (_versaoDasPaginas != null)
            return _versaoDasPaginas;
        using var soma = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string arquivo in Arquivos.Values.Select(a => a.Arquivo).Distinct().Order(StringComparer.Ordinal))
        {
            string caminho = Path.Combine(_pasta, arquivo);
            soma.AppendData(Encoding.UTF8.GetBytes(arquivo));
            if (File.Exists(caminho))
                soma.AppendData(File.ReadAllBytes(caminho));
        }
        return _versaoDasPaginas = Convert.ToHexString(soma.GetHashAndReset())[..12];
    }

    private static async Task EscreverAsync(Stream saida, string texto, CancellationToken cancelar)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(texto);
        await saida.WriteAsync(bytes, cancelar).ConfigureAwait(false);
        await saida.FlushAsync(cancelar).ConfigureAwait(false);
    }

    // Uma rodada antiga (outra porta, já desligada) não muda o estado
    private void Mudar(EstadoDoOverlay estado, CancellationTokenSource? rodada)
    {
        lock (_trava)
        {
            if (rodada != null && rodada != _rodando)
                return;
            if (Estado == estado)
                return;
            Estado = estado;
        }
        EstadoMudou?.Invoke(estado);
    }
}
