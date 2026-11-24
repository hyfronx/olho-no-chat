using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas;
using OlhoNoChat.Janelas.Configuracoes;
using Xunit;

namespace OlhoNoChat.Testes.Janelas;

public class EscalaDaInterfaceTestes
{
    // O automático: Full HD ou mais fica em 100%; telas menores encolhem na proporção, nunca abaixo de 75%
    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 1.0)]
    [InlineData(3840, 2160, 1.0)]
    [InlineData(1600, 900, 0.85)]
    [InlineData(1280, 960, 0.75)]
    [InlineData(1366, 768, 0.75)]
    [InlineData(1280, 720, 0.75)]
    [InlineData(800, 600, 0.75)]
    [InlineData(0, 0, 1.0)]
    public void Automatico_PeloTamanhoDaTela(double largura, double altura, double esperado) =>
        Assert.Equal(esperado, EscalaDaInterface.Automatico(largura, altura), 3);

    // Uma tela 4K com o Windows em 150% conta como 2560 × 1440; em 300%, como 1280 × 720
    [Fact]
    public void Automatico_UsaATelaJaDivididaPelaEscalaDoWindows()
    {
        Assert.Equal(1.0, EscalaDaInterface.Automatico(3840 / 1.5, 2160 / 1.5), 3);
        Assert.Equal(0.75, EscalaDaInterface.Automatico(3840 / 3.0, 2160 / 3.0), 3);
    }

    [Theory]
    [InlineData(0, 0.75)]     // automático
    [InlineData(-1, 0.75)]    // sem sentido: automático
    [InlineData(1.0, 1.0)]    // escolhido: a tela não importa
    [InlineData(0.7, 0.7)]
    [InlineData(1.3, 1.3)]
    [InlineData(0.5, 0.7)]    // fora dos limites: o mais perto
    [InlineData(2.0, 1.3)]
    [InlineData(0.83, 0.85)]  // de 5 em 5
    public void Calcular_OEscolhidoOuOAutomatico(double opcao, double esperado) =>
        Assert.Equal(esperado, EscalaDaInterface.Calcular(opcao, 1280, 960), 3);

    // O deslizante da aba Aparência: a posição 0 é "Automático"; as outras vão de 70% a 130%
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.7, 1)]
    [InlineData(0.75, 2)]
    [InlineData(1.0, 7)]
    [InlineData(1.3, 13)]
    public void Deslizante_PosicaoEValorVaoEVoltam(double tamanho, int posicao)
    {
        Assert.Equal(posicao, LogicaAparencia.PosicaoDoTamanhoDaInterface(tamanho));
        Assert.Equal(tamanho, LogicaAparencia.TamanhoDaInterfaceDaPosicao(posicao), 3);
    }

    [Fact]
    public void Deslizante_MostraAutomaticoOuAPorcentagem()
    {
        var logica = new LogicaAparencia { PosicaoDaInterface = 0 };
        Assert.Equal("Automático", logica.TextoDaInterface);
        logica.PosicaoDaInterface = 4;
        Assert.Equal("85%", logica.TextoDaInterface);
        Assert.Equal(13, logica.UltimaPosicaoDaInterface);
    }

    // A posição gravada num monitor em 125%: o lugar em pixels é o número gravado vezes a escala gravada com ele
    [Fact]
    public void PosicaoGravada_EmPixelsPelaEscalaGravada()
    {
        var gravada = new PosicaoDaJanela(-914.4, 72.8, 420, 400, System.Windows.WindowState.Normal, 1.25);
        Assert.Equal(OlhoNoChat.Sistema.Retangulo.De(-1143, 91, 525, 500), gravada.EmPixels());
    }

    // Gravada por uma versão que não guardava a escala: sem lugar em pixels (a janela abre como o WPF a criar)
    [Fact]
    public void PosicaoGravada_SemEscalaNaoTemPixels() =>
        Assert.Null(new PosicaoDaJanela(100, 50, 420, 400, System.Windows.WindowState.Normal).EmPixels());

    // Arquivo editado à mão: fora dos limites vai para o mais perto, sem sentido volta para o automático
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.9, 0.9)]
    [InlineData(0.2, 0.7)]
    [InlineData(5, 1.3)]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    public void Opcoes_CorrigemOTamanhoDaInterface(double gravado, double esperado)
    {
        var opcoes = new Opcoes { TamanhoDaInterface = gravado };
        opcoes.Corrigir();
        Assert.Equal(esperado, opcoes.TamanhoDaInterface, 3);
    }
}
