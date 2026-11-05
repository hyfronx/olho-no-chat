using OlhoNoChat.Kick;
using static OlhoNoChat.Testes.Kick.AmostrasDaKick;

namespace OlhoNoChat.Testes.Kick;

/// <summary>Os eventos da sala: a mensagem (texto, emotes, cor, papéis e selos na ordem do site) e o que tira linhas.</summary>
public class EventosDaKickTestes
{
    private static readonly SalaDaKick Sala = SalaDaKick.Ler(Canal);

    private static ItemDaKick Mensagem(string dados) =>
        EventosDaKick.Ler(EventosDaKick.Mensagem, dados, Sala)?.Mensagem ?? throw new Xunit.Sdk.XunitException("sem mensagem");

    [Fact]
    public void Mensagem_TextoEmotesECor()
    {
        ItemDaKick item = Mensagem(DadosDaMensagem());

        Assert.Equal("3873ad9f-1750-4649-95b9-9f8feef61198", item.Id);
        Assert.Equal("viewer1", item.Login);
        Assert.Equal("Viewer1", item.Nome);
        Assert.Equal("#1475E1", item.Cor);
        Assert.Equal(
            [new ParteDaKick("boa "), new ParteDaKick("KEKW", "https://files.kick.com/emotes/37226/fullsize"), new ParteDaKick(" demais")],
            item.Partes);
    }

    [Fact]
    public void Selos_NovosEscolhidos_DepoisOsAntigos_NaOrdemDoSite()
    {
        ItemDaKick item = Mensagem(DadosDaMensagem());

        // O de nível primeiro (sort_order 1), os outros escolhidos como vieram, o Ghost (não escolhido) fora; depois a
        // insígnia de assinante de 13 meses do canal (a de 12). O de presenteador não aparece, como antes
        Assert.Equal(["Nível 27", "GoldenK", "WorldofWarcraftForeverBeta", "Assinante"], item.Selos.Select(s => s.Nome));
        Assert.Equal("https://ext.cdn.kick.com/chat/badges/27_16ee146e.png", item.Selos[0].Imagem);
        Assert.Contains("/553620/", item.Selos[3].Imagem);
        Assert.Empty(item.Papeis);
    }

    [Fact]
    public void DonoModeradorEVip_ValemParaOsFiltros_ComOsSelosDaTwitch()
    {
        ItemDaKick item = Mensagem(DadosDaMensagem(selos:
            """[{"type":"vip","sort_order":3},{"type":"broadcaster","sort_order":1},{"type":"moderator","sort_order":2},{"type":"verified","sort_order":5}]""",
            selosNovos: "[]"));

        Assert.Equal(["broadcaster", "moderator", "vip"], item.Papeis);
        Assert.Equal(["broadcaster/1", "moderator/1", "vip/1"], item.Selos.Select(s => s.DaTwitch));
    }

    [Fact]
    public void SeloNovoDeOutroEndereco_NaoAparece()
    {
        ItemDaKick item = Mensagem(DadosDaMensagem(selos: "[]", selosNovos:
            """[{"name":"x","image_url":"https://exemplo.com/x.png","selected":true,"sort_order":4},{"name":"y","image_url":"http://ext.cdn.kick.com/y.png","selected":true}]"""));

        Assert.Empty(item.Selos);
    }

    [Fact]
    public void SemTextoOuSemNome_Ignorada()
    {
        Assert.Null(EventosDaKick.Ler(EventosDaKick.Mensagem, DadosDaMensagem(texto: ""), Sala));
        Assert.Null(EventosDaKick.Ler(EventosDaKick.Mensagem, """{"id":"1","content":"oi","sender":{}}""", Sala));
    }

    [Fact]
    public void Apagada_Banido_ELimpeza()
    {
        Assert.Equal(new EventoDaKick(TipoDeEventoDaKick.Apagada, Id: "m1"),
            EventosDaKick.Ler(EventosDaKick.Apagada, """{"id":"x","message":{"id":"m1"}}""", Sala));
        Assert.Equal(new EventoDaKick(TipoDeEventoDaKick.Banido, Login: "viewer2"),
            EventosDaKick.Ler(EventosDaKick.Banido,
                """{"id":"y","user":{"id":2,"username":"Viewer2","slug":"viewer2"},"banned_by":{"id":3,"username":"viewer3"}}""", Sala));
        Assert.Equal(new EventoDaKick(TipoDeEventoDaKick.Limpeza), EventosDaKick.Ler(EventosDaKick.Limpeza, "{}", Sala));
    }

    [Fact]
    public void OutroEvento_Ignorado()
    {
        Assert.Null(EventosDaKick.Ler(@"App\Events\PollUpdateEvent", "{}", Sala));
    }
}
