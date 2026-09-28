using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public class EnvioDeMensagemTestes
{
    private static EnvioDeMensagem Envio(TwitchFalsa twitch, ContaSalvaNaMemoria salva)
    {
        var api = new ApiDaTwitch(twitch);
        var conta = new ContaDaTwitch(api, salva, NullLogger<ContaDaTwitch>.Instance);
        return new EnvioDeMensagem(api, conta, NullLogger<EnvioDeMensagem>.Instance);
    }

    private static TwitchFalsa TwitchComCanal() => new TwitchFalsa()
        .Quando("GET", "users?login=canal", HttpStatusCode.OK, """{"data":[{"id":"999","login":"canal"}]}""");

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"data":[{"message_id":"x","is_sent":true}]}""", EnvioDeMensagem.Situacao.Enviada, "")]
    [InlineData(HttpStatusCode.OK, """{"data":[{"message_id":"","is_sent":false,"drop_reason":{"code":"msg_duplicate","message":"Sua mensagem é igual à anterior."}}]}""",
        EnvioDeMensagem.Situacao.NaoPublicada, "A Twitch não publicou a mensagem: Sua mensagem é igual à anterior.")]
    [InlineData(HttpStatusCode.OK, """{"data":[{"message_id":"","is_sent":false}]}""", EnvioDeMensagem.Situacao.NaoPublicada, "A Twitch não publicou a mensagem.")]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":401,"message":"Invalid OAuth token"}""", EnvioDeMensagem.Situacao.SemConta,
        "A conexão com a Twitch expirou. Conecte de novo nas Configurações (aba Twitch).")]
    [InlineData(HttpStatusCode.Forbidden, "{}", EnvioDeMensagem.Situacao.NaoPublicada,
        "A Twitch não deixou enviar nesse chat (você pode estar banido ou suspenso nele).")]
    [InlineData(HttpStatusCode.TooManyRequests, "", EnvioDeMensagem.Situacao.NaoPublicada, "Muitas mensagens seguidas. Espere um pouco e tente de novo.")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"Bad Request","status":400,"message":"The message field is required."}""",
        EnvioDeMensagem.Situacao.Falhou, "Não foi possível enviar (400: The message field is required.).")]
    [InlineData(HttpStatusCode.InternalServerError, "", EnvioDeMensagem.Situacao.Falhou, "Não foi possível enviar (500).")]
    public async Task Enviar_TextoParaCadaRespostaDaTwitch(HttpStatusCode codigo, string corpo, EnvioDeMensagem.Situacao situacao, string texto)
    {
        var twitch = TwitchComCanal().Quando("POST", "chat/messages", codigo, corpo)
                                     .Quando("GET", "validate", HttpStatusCode.OK, Montar.ValidacaoOk);
        var resultado = await Envio(twitch, Montar.ContaConectada()).EnviarAsync("canal", "oi");

        Assert.Equal(situacao, resultado.Situacao);
        Assert.Equal(texto, resultado.Texto);
    }

    [Fact]
    public async Task Enviar_MandaCanalContaETexto()
    {
        var twitch = TwitchComCanal().Quando("POST", "chat/messages", HttpStatusCode.OK, """{"data":[{"is_sent":true}]}""");
        await Envio(twitch, Montar.ContaConectada()).EnviarAsync("canal", "olá \"mundo\"");

        var envio = twitch.PedidosPara("chat/messages").Single();
        var json = JsonNode.Parse(envio.Corpo)!;
        Assert.Equal("999", (string?)json["broadcaster_id"]);
        Assert.Equal("121292674", (string?)json["sender_id"]);
        Assert.Equal("olá \"mundo\"", (string?)json["message"]);
        Assert.Equal("Bearer tok", envio.Autorizacao);
    }

    [Fact]
    public async Task Enviar_IdDoCanalEGuardadoSemDiferenciarMaiusculas()
    {
        var twitch = TwitchComCanal().Quando("POST", "chat/messages", HttpStatusCode.OK, """{"data":[{"is_sent":true}]}""");
        var envio = Envio(twitch, Montar.ContaConectada());

        await envio.EnviarAsync("canal", "1");
        await envio.EnviarAsync("CANAL", "2");
        Assert.Single(twitch.PedidosPara("users?login="));
    }

    [Fact]
    public async Task Enviar_401ConfereOAcessoDeNovo()
    {
        var twitch = TwitchComCanal().Quando("POST", "chat/messages", HttpStatusCode.Unauthorized)
                                     .Quando("GET", "validate", HttpStatusCode.Unauthorized);
        var salva = Montar.ContaConectada();
        await Envio(twitch, salva).EnviarAsync("canal", "oi");

        await ContaDaTwitchTestes.EsperarAsync(() => salva.Token == "");
    }

    [Fact]
    public async Task Enviar_SemConta()
    {
        var resultado = await Envio(new TwitchFalsa(), new ContaSalvaNaMemoria()).EnviarAsync("canal", "oi");
        Assert.Equal(EnvioDeMensagem.Situacao.SemConta, resultado.Situacao);
        Assert.Equal("Conecte sua conta da Twitch nas Configurações (aba Twitch).", resultado.Texto);
    }

    [Fact]
    public async Task Enviar_CanalQueNaoExiste()
    {
        var twitch = new TwitchFalsa().Quando("GET", "users?login=", HttpStatusCode.OK, """{"data":[]}""");
        var resultado = await Envio(twitch, Montar.ContaConectada()).EnviarAsync("fantasma", "oi");
        Assert.Equal("Não achei o canal \"fantasma\" na Twitch.", resultado.Texto);
        Assert.Empty(twitch.PedidosPara("chat/messages"));
    }

    [Fact]
    public async Task Enviar_SemInternet()
    {
        var resultado = await Envio(new TwitchFalsa(), Montar.ContaConectada()).EnviarAsync("canal", "oi");
        Assert.Equal(EnvioDeMensagem.Situacao.Falhou, resultado.Situacao);
        Assert.Equal("Não foi possível enviar. Confira sua internet.", resultado.Texto);
    }
}
