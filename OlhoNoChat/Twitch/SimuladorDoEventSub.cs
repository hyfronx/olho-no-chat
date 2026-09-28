#if DEBUG
#nullable enable
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// Só na versão de teste: com a variável OLHONOCHAT_SIMULADOR_EVENTSUB=ws://127.0.0.1:8080/ws, os resgates conectam ao
/// servidor falso da Twitch CLI ("twitch event websocket start-server") e assinam nele, e "twitch event trigger" manda
/// resgates de mentira. O resto (conta, envio, emotes) continua na Twitch de verdade.
/// </summary>
internal sealed class SimuladorDoEventSub : DelegatingHandler
{
    public const string Variavel = "OLHONOCHAT_SIMULADOR_EVENTSUB";
    private const string AssinaturasDaTwitch = "https://api.twitch.tv/helix/eventsub/subscriptions";

    private readonly Uri _assinaturas;

    private SimuladorDoEventSub(Uri simulador) : base(new SocketsHttpHandler())
    {
        _assinaturas = new Uri($"http://{simulador.Host}:{simulador.Port}/eventsub/subscriptions");
    }

    /// <summary>Troca a API e os resgates registrados antes, se a variável estiver definida.</summary>
    public static void RegistrarSeLigado(IServiceCollection servicos)
    {
        if (Environment.GetEnvironmentVariable(Variavel) is not { Length: > 0 } endereco)
            return;
        var simulador = new Uri(endereco);
        servicos.AddSingleton(new ApiDaTwitch(new SimuladorDoEventSub(simulador)));
        servicos.AddSingleton(provedor => new ResgatesDePontos(provedor.GetRequiredService<ApiDaTwitch>(),
            provedor.GetRequiredService<ContaDaTwitch>(), provedor.GetRequiredService<ILogger<ResgatesDePontos>>(),
            simulador, ResgatesDePontos.EsperaPadrao));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelar)
    {
        if (pedido.RequestUri?.ToString().StartsWith(AssinaturasDaTwitch, StringComparison.Ordinal) == true)
            pedido.RequestUri = _assinaturas;
        return base.SendAsync(pedido, cancelar);
    }
}
#endif
