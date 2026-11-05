using System.Text.Json;
using OlhoNoChat.Kick;
using static OlhoNoChat.Testes.Kick.AmostrasDaKick;

namespace OlhoNoChat.Testes.Kick;

/// <summary>O canal da Kick pela API do site e as respostas do tempo real (token e endereço do Centrifugo).</summary>
public class SalaDaKickTestes
{
    [Fact]
    public void Le_OsIdsEAsInsigniasEmOrdem()
    {
        SalaDaKick sala = SalaDaKick.Ler(Canal);

        Assert.Equal(IdDoCanal, sala.IdDoCanal);
        Assert.Equal(IdDaSala, sala.IdDaSala);
        Assert.Equal([1, 2, 12], sala.Insignias.Select(i => i.Meses));
    }

    [Theory]
    [InlineData(0, "553609")]   // menos que a primeira: a primeira
    [InlineData(1, "553609")]
    [InlineData(5, "553611")]   // a dos mais meses alcançados
    [InlineData(40, "553620")]
    public void Insignia_DosMesesAlcancados(int meses, string esperada)
    {
        Assert.Contains("/" + esperada + "/", SalaDaKick.Ler(Canal).InsigniaPara(meses));
    }

    [Fact]
    public void SemInsignias_Null()
    {
        Assert.Null(SalaDaKick.Ler("""{"id":1,"chatroom":{"id":2}}""").InsigniaPara(3));
    }

    [Theory]
    [InlineData("""{"message":"Not found"}""")]
    [InlineData("""{"id":1,"chatroom":null}""")]
    [InlineData("[]")]
    public void RespostaInesperada_Erro(string json)
    {
        Assert.ThrowsAny<JsonException>(() => SalaDaKick.Ler(json));
    }

    [Fact]
    public void Endereco_ComONome()
    {
        Assert.Equal("https://kick.com/api/v2/channels/seu-canal", SalaDaKick.Endereco("seu-canal"));
    }

    [Fact]
    public void Token_EEnderecoDoCentrifugo()
    {
        Assert.Equal("eyJhbGciOi.teste.assinatura", ConexaoComAKick.LerToken(Token));
        Assert.Equal("wss://realtime.us-east-1.platform.kick.com/connection/websocket", ConexaoComAKick.LerEndereco(Endereco));
    }

    [Theory]
    [InlineData("""{"data":{"connections":[{"credentials":{"app_key":"abc","cluster":"us2"},"provider":"pusher"}]}}""")]
    [InlineData("""{"data":{"connections":[]}}""")]
    [InlineData("""{"message":"error"}""")]
    public void SemCentrifugo_Erro(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConexaoComAKick.LerEndereco(json));
    }

    [Fact]
    public void SemToken_Erro()
    {
        Assert.ThrowsAny<JsonException>(() => ConexaoComAKick.LerToken("""{"data":{}}"""));
    }
}
