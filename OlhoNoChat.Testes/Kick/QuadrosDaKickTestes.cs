using OlhoNoChat.Kick;
using static OlhoNoChat.Testes.Kick.AmostrasDaKick;

namespace OlhoNoChat.Testes.Kick;

/// <summary>O protocolo do Centrifugo: o que mandamos e como as linhas que chegam são lidas.</summary>
public class QuadrosDaKickTestes
{
    [Fact]
    public void ConectarEAssinar_DuasLinhasNumQuadro()
    {
        Assert.Equal(
            """{"id":1,"connect":{"token":"tok","name":"js"}}""" + "\n" + """{"id":2,"subscribe":{"channel":"chatrooms.66973867.v2"}}""",
            QuadrosDaKick.ConectarEAssinar("tok", IdDaSala));
    }

    [Fact]
    public void Renovar_ComONumeroDoPedido()
    {
        Assert.Equal("""{"id":3,"refresh":{"token":"novo"}}""", QuadrosDaKick.Renovar(3, "novo"));
    }

    [Fact]
    public void VariasLinhas_NumQuadro()
    {
        IReadOnlyList<LinhaDaKick> linhas = QuadrosDaKick.Ler(Conectou + "\n" + Assinou + "\n");

        Assert.Equal([TipoDeLinhaDaKick.Conectou, TipoDeLinhaDaKick.Assinou], linhas.Select(l => l.Tipo));
        Assert.Equal(2699, linhas[0].Ttl);
        Assert.Equal(QuadrosDaKick.PedidoAssinar, linhas[1].Pedido);
    }

    [Fact]
    public void Ping_EUmaLinhaVazia()
    {
        Assert.Equal(TipoDeLinhaDaKick.Ping, Assert.Single(QuadrosDaKick.Ler("{}")).Tipo);
    }

    [Fact]
    public void Evento_ComODataEmTexto()
    {
        LinhaDaKick linha = Assert.Single(QuadrosDaKick.Ler(Mensagem("m1", "Viewer1", "oi")));

        Assert.Equal(TipoDeLinhaDaKick.Evento, linha.Tipo);
        Assert.Equal(EventosDaKick.Mensagem, linha.Evento);
        Assert.Contains("\"content\":\"oi\"", linha.Dados);
    }

    [Fact]
    public void ErroERenovacaoEDesconectar()
    {
        IReadOnlyList<LinhaDaKick> linhas = QuadrosDaKick.Ler(
            """{"id":1,"error":{"code":109,"message":"token expired"}}""" + "\n" +
            """{"id":3,"refresh":{"expires":true,"ttl":2697}}""" + "\n" +
            """{"push":{"disconnect":{"code":3005,"reason":"stale"}}}""");

        Assert.Equal([TipoDeLinhaDaKick.Erro, TipoDeLinhaDaKick.Renovou, TipoDeLinhaDaKick.Desconectar], linhas.Select(l => l.Tipo));
        Assert.Equal((1, 109, "token expired"), (linhas[0].Pedido, linhas[0].Codigo, linhas[0].Texto));
        Assert.Equal(2697, linhas[1].Ttl);
        Assert.Equal((3005, "stale"), (linhas[2].Codigo, linhas[2].Texto));
    }

    [Fact]
    public void LinhaQueNaoEJson_Outro()
    {
        Assert.Equal(TipoDeLinhaDaKick.Outro, Assert.Single(QuadrosDaKick.Ler("não é json")).Tipo);
    }
}
