using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Testes.Janelas;

public class PosicaoEBordasTestes
{
    private static readonly Retangulo Principal = new(0, 0, 2560, 1400);
    private static readonly Retangulo DaEsquerda = new(-1920, 0, 0, 1040);
    private static readonly IReadOnlyList<Retangulo> Monitores = [Principal, DaEsquerda];

    [Fact]
    public void PrimeiraAbertura_NoCentroDoMonitorPrincipal_Decisao18()
    {
        Retangulo? lugar = PosicaoNaTela.AoAbrir(Retangulo.De(-1500, 300, 360, 530), primeiraVez: true, Monitores, Principal);

        Assert.Equal(Retangulo.De(1100, 435, 360, 530), lugar);
    }

    [Fact]
    public void PosicaoGravadaNumMonitor_FicaOndeEstava()
    {
        Assert.Null(PosicaoNaTela.AoAbrir(Retangulo.De(1956, 232, 431, 420), primeiraVez: false, Monitores, Principal));
        Assert.Null(PosicaoNaTela.AoAbrir(Retangulo.De(-1800, 100, 400, 500), primeiraVez: false, Monitores, Principal));
    }

    [Fact]
    public void PosicaoForaDeQualquerMonitor_VoltaParaOCentroDoPrincipal_Decisao7()
    {
        // O monitor da esquerda foi desligado: a janela ficou em x negativo
        Retangulo? lugar = PosicaoNaTela.AoAbrir(Retangulo.De(-1800, 100, 400, 500), primeiraVez: false, [Principal], Principal);
        Assert.Equal(Retangulo.De(1080, 450, 400, 500), lugar);

        // Muito longe, embaixo
        Assert.NotNull(PosicaoNaTela.AoAbrir(Retangulo.De(100, 5000, 400, 500), primeiraVez: false, Monitores, Principal));
    }

    [Fact]
    public void SoUmPedacinhoNaTela_ContaComoFora()
    {
        // Só 20 px da janela na borda direita do monitor principal: não dá para pegar a barra
        Assert.False(PosicaoNaTela.EstaVisivel(Retangulo.De(2540, 100, 400, 500), [Principal]));
        Assert.True(PosicaoNaTela.EstaVisivel(Retangulo.De(2500, 100, 400, 500), [Principal]));
        Assert.False(PosicaoNaTela.EstaVisivel(Retangulo.De(100, 1380, 400, 500), [Principal]));
    }

    [Fact]
    public void NoCentro_JanelaMaiorQueAAreaEncolhe()
    {
        Assert.Equal(new Retangulo(0, 0, 800, 600), PosicaoNaTela.NoCentro(Retangulo.De(50, 50, 1000, 900), new Retangulo(0, 0, 800, 600)));
    }

    [Theory]
    [InlineData(200, 200, BordasDaJanela.Nenhuma)]
    [InlineData(2, 200, BordasDaJanela.Esquerda)]
    [InlineData(355, 200, BordasDaJanela.Direita)]
    [InlineData(200, 1, BordasDaJanela.Cima)]
    [InlineData(200, 5, BordasDaJanela.Nenhuma)] // a borda de cima é mais fina: logo abaixo já é a barra
    [InlineData(200, 527, BordasDaJanela.Baixo)]
    [InlineData(2, 1, BordasDaJanela.CimaEsquerda)]
    [InlineData(10, 2, BordasDaJanela.CimaEsquerda)]
    [InlineData(350, 2, BordasDaJanela.CimaDireita)]
    [InlineData(2, 520, BordasDaJanela.BaixoEsquerda)]
    [InlineData(346, 516, BordasDaJanela.BaixoDireita)] // o quadrado inteiro de 16 px
    [InlineData(359, 529, BordasDaJanela.BaixoDireita)]
    [InlineData(343, 516, BordasDaJanela.Nenhuma)]
    [InlineData(-1, 200, BordasDaJanela.Nenhuma)]
    public void Bordas_QualParteEstaNoPonto(double x, double y, int esperada)
    {
        Assert.Equal(esperada, BordasDaJanela.Parte(x, y, 360, 530));
    }
}
