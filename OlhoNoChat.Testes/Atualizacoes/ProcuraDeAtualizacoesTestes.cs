using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Atualizacoes;

public class ProcuraDeAtualizacoesTestes
{
    private readonly Opcoes _opcoes = new();
    private int _gravacoes;
    private int _avisos;

    private ProcuraDeAtualizacoes Nova()
    {
        var procura = new ProcuraDeAtualizacoes(NullLogger<ProcuraDeAtualizacoes>.Instance, () => _opcoes, () => { _gravacoes++; return true; });
        procura.ProcuraAutomaticaDesligada += () => _avisos++;
        return procura;
    }

    [Fact]
    public void Depois_ComNaoProcurarMarcadaDesligaGravaNaHoraEAvisa()
    {
        Nova().DepoisDoAviso(naoProcurarAutomaticamente: true);

        Assert.False(_opcoes.ProcurarAtualizacoes);
        Assert.Equal(1, _gravacoes);
        Assert.Equal(1, _avisos);
    }

    [Fact]
    public void Depois_SemMarcarNaoMudaNada()
    {
        Nova().DepoisDoAviso(naoProcurarAutomaticamente: false);

        Assert.True(_opcoes.ProcurarAtualizacoes);
        Assert.Equal(0, _gravacoes);
        Assert.Equal(0, _avisos);
    }

    [Fact]
    public void Depois_JaDesligadaNaoGravaDeNovo()
    {
        // Procura manual com a automática já desligada
        _opcoes.ProcurarAtualizacoes = false;

        Nova().DepoisDoAviso(naoProcurarAutomaticamente: true);

        Assert.Equal(0, _gravacoes);
        Assert.Equal(0, _avisos);
    }
}
