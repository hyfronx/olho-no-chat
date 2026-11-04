using OlhoNoChat.Atualizacoes;
using OlhoNoChat.Janelas.Configuracoes;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaGeralTestes
{
    [Theory]
    [InlineData(EstadoDaProcura.Parada, "Procurar agora", false, true)]
    [InlineData(EstadoDaProcura.Procurando, "Procurando…", true, false)]
    [InlineData(EstadoDaProcura.Baixando, "Baixando…", true, false)]
    public void ProcurarAgora_MostraAProcuraEmAndamento(EstadoDaProcura procura, string texto, bool girando, bool aceitaClique)
    {
        var geral = new LogicaGeral { Procura = procura };

        Assert.Equal(texto, geral.TextoDoProcurarAgora);
        Assert.Equal(girando, geral.Procurando);
        Assert.Equal(aceitaClique, geral.ProcurarAgoraCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(0.5, "0,5 linha/s")]
    [InlineData(1, "1 linha/s")]
    [InlineData(1.5, "1,5 linha/s")]
    [InlineData(2, "2 linhas/s")]
    [InlineData(6, "6 linhas/s")]
    public void VelocidadeDeContinuar_EmLinhasPorSegundo(double valor, string texto)
    {
        Assert.Equal(texto, LogicaChat.LinhasPorSegundo(valor));
    }

    [Fact]
    public void ProcurarAgora_EnquantoProcuraNaoPedeDeNovo()
    {
        var geral = new LogicaGeral();
        int pedidos = 0;
        geral.ProcurarAtualizacoesPedido += () => pedidos++;
        var avisos = new List<string?>();
        geral.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);
        bool podeMudou = false;
        geral.ProcurarAgoraCommand.CanExecuteChanged += (_, _) => podeMudou = true;

        geral.ProcurarAgoraCommand.Execute(null);
        geral.Procura = EstadoDaProcura.Procurando;

        Assert.Equal(1, pedidos);
        Assert.False(geral.ProcurarAgoraCommand.CanExecute(null));
        Assert.True(podeMudou); // o botão fica desativado na hora
        Assert.Contains(nameof(LogicaGeral.TextoDoProcurarAgora), avisos);
        Assert.Contains(nameof(LogicaGeral.Procurando), avisos);
    }
}
