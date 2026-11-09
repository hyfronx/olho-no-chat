using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Kick;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;

namespace OlhoNoChat.Chat;

/// <summary>Os pedidos de quantos assistem (os testes usam um falso). null = fora do ar; erros saem como exceção.</summary>
public interface IFontesDeViewers
{
    Task<long?> TwitchAsync(string canal, CancellationToken cancelar);

    Task<long?> YouTubeAsync(PaginaDaLive live, CancellationToken cancelar);

    Task<long?> KickAsync(string canal, CancellationToken cancelar);
}

/// <summary>
/// "Mostrar viewers": pergunta a cada <see cref="Intervalo"/> quantos assistem o canal da Twitch, a live do YouTube e o
/// canal da Kick. O TikTok não é perguntado: o número chega pela conexão do chat (<see cref="TikTok"/>). Nada disso pede
/// conta. Uma plataforma que não respondeu fica sem número até a próxima vez.
/// </summary>
public sealed class ContagemDeViewers : IDisposable
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    private readonly ILogger _log;
    private readonly IFontesDeViewers _fontes;
    private readonly Func<PaginaDaLive?> _liveDoYouTube;
    private readonly TimeSpan _intervalo;
    private readonly object _trava = new();
    private CancellationTokenSource? _contagem;
    private (string? Twitch, bool YouTube, string? Kick, bool TikTok) _oQueContar;

    /// <param name="liveDoYouTube">A live do YouTube sendo lida agora (<see cref="LeitorDoYouTube.LiveAtual"/>).</param>
    public ContagemDeViewers(ILogger log, IFontesDeViewers fontes, Func<PaginaDaLive?> liveDoYouTube, TimeSpan? intervalo = null)
    {
        _log = log;
        _fontes = fontes;
        _liveDoYouTube = liveDoYouTube;
        _intervalo = intervalo ?? Intervalo;
    }

    /// <summary>Os números mudaram (em outra thread).</summary>
    public event Action<Viewers>? Mudou;

    public Viewers Atual { get; private set; } = Viewers.Nenhum;

    /// <summary>
    /// O que contar: o canal da Twitch, a live do YouTube (a que o leitor está lendo), o canal da Kick e o TikTok (null
    /// / false = não conta). Tudo vazio para de contar. O que mudou é perguntado na hora.
    /// </summary>
    public void Contar(string? twitch, bool youTube, string? kick, bool tikTok)
    {
        var novo = (twitch, youTube, kick, tikTok);
        Viewers? mudou;
        lock (_trava)
        {
            if (novo == _oQueContar && (_contagem != null || !Ligada(novo)))
                return;
            _oQueContar = novo;
            // O que deixou de ser contado some na hora
            mudou = Trocar(new Viewers(
                twitch != null ? Atual.Twitch : null,
                youTube ? Atual.YouTube : null,
                kick != null ? Atual.Kick : null,
                tikTok ? Atual.TikTok : null));
            Recomecar();
        }
        Avisar(mudou);
    }

    /// <summary>Pergunta de novo agora (uma live do YouTube começou a ser lida, outro canal...).</summary>
    public void ContarAgora()
    {
        lock (_trava)
        {
            if (_contagem != null)
                Recomecar();
        }
    }

    /// <summary>O número do TikTok que chegou pela conexão do chat (null = saiu da live).</summary>
    public void TikTok(long? viewers)
    {
        Viewers? mudou;
        lock (_trava)
        {
            if (!_oQueContar.TikTok && viewers != null)
                return;
            mudou = Trocar(Atual with { TikTok = viewers });
        }
        Avisar(mudou);
    }

    public void Dispose() => Contar(null, false, null, false);

    private static bool Ligada((string? Twitch, bool YouTube, string? Kick, bool TikTok) o) =>
        o.Twitch != null || o.YouTube || o.Kick != null;

    // Com a trava: para a contagem que estava (sem esperar) e começa outra, se tem o que perguntar
    private void Recomecar()
    {
        _contagem?.Cancel();
        _contagem = null;
        if (!Ligada(_oQueContar))
            return;
        var contagem = new CancellationTokenSource();
        _contagem = contagem;
        var oQue = _oQueContar;
        _ = Task.Run(() => ContarAsync(oQue, contagem));
    }

    private async Task ContarAsync((string? Twitch, bool YouTube, string? Kick, bool TikTok) oQue, CancellationTokenSource contagem)
    {
        CancellationToken cancelar = contagem.Token;
        try
        {
            while (true)
            {
                PaginaDaLive? live = oQue.YouTube ? _liveDoYouTube() : null;
                Task<long?> twitch = Perguntar("Twitch", oQue.Twitch, c => _fontes.TwitchAsync(c, cancelar), cancelar);
                Task<long?> youTube = Perguntar("YouTube", live, l => _fontes.YouTubeAsync(l, cancelar), cancelar);
                Task<long?> kick = Perguntar("Kick", oQue.Kick, c => _fontes.KickAsync(c, cancelar), cancelar);
                long?[] numeros = await Task.WhenAll(twitch, youTube, kick).ConfigureAwait(false);

                Viewers? mudou;
                lock (_trava)
                {
                    if (contagem != _contagem)
                        return;
                    mudou = Trocar(Atual with { Twitch = numeros[0], YouTube = numeros[1], Kick = numeros[2] });
                }
                Avisar(mudou);
                await Task.Delay(_intervalo, cancelar).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
        }
    }

    private async Task<long?> Perguntar<T>(string plataforma, T? deQuem, Func<T, Task<long?>> pedido, CancellationToken cancelar) where T : class
    {
        if (deQuem == null)
            return null;
        try
        {
            return await pedido(deQuem).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancelar.IsCancellationRequested)
        {
            _log.LogDebug("Viewers: {Plataforma} não respondeu: {Erro}", plataforma, e.Message);
            return null;
        }
    }

    // Com a trava: o novo, se mudou
    private Viewers? Trocar(Viewers novo)
    {
        if (novo == Atual)
            return null;
        Atual = novo;
        return novo;
    }

    private void Avisar(Viewers? mudou)
    {
        if (mudou != null)
            Mudou?.Invoke(mudou);
    }
}

/// <summary>Os pedidos de verdade, sem conta e sem cookies.</summary>
public sealed class FontesDeViewers : IFontesDeViewers
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly IConexaoComAKick _kick = new ConexaoComAKick();
    // O endereço que deu certo para o canal da Kick ("fulano_1" ou "fulano-1", ver CanalDaKick.OutraForma)
    private (string Canal, string Endereco)? _formaDaKick;

    public async Task<long?> TwitchAsync(string canal, CancellationToken cancelar)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, ViewersDaTwitch.Endereco)
        {
            Content = new StringContent(ViewersDaTwitch.Pedido(canal), Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Add("Client-Id", ViewersDaTwitch.IdDoSite);
        return ViewersDaTwitch.Ler(await EnviarAsync(pedido, cancelar).ConfigureAwait(false));
    }

    public async Task<long?> YouTubeAsync(PaginaDaLive live, CancellationToken cancelar)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post,
            "https://www.youtube.com" + ViewersDoYouTube.Caminho + Uri.EscapeDataString(live.Chave))
        {
            Content = new StringContent(ViewersDoYouTube.Pedido(live), Encoding.UTF8, "application/json"),
        };
        return ViewersDoYouTube.Ler(await EnviarAsync(pedido, cancelar).ConfigureAwait(false));
    }

    public async Task<long?> KickAsync(string canal, CancellationToken cancelar)
    {
        string endereco = _formaDaKick is { } forma && forma.Canal == canal ? forma.Endereco : canal;
        string? json = await _kick.CanalAsync(endereco, cancelar).ConfigureAwait(false);
        if (json == null && CanalDaKick.OutraForma(endereco) is { } outra)
        {
            json = await _kick.CanalAsync(outra, cancelar).ConfigureAwait(false);
            endereco = outra;
        }
        if (json == null)
            return null;
        _formaDaKick = (canal, endereco);
        return SalaDaKick.LerViewers(json);
    }

    private static async Task<string> EnviarAsync(HttpRequestMessage pedido, CancellationToken cancelar)
    {
        using HttpResponseMessage resposta = await Http.SendAsync(pedido, cancelar).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cancelar).ConfigureAwait(false);
    }
}
