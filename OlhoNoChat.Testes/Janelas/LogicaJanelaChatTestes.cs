using OlhoNoChat.Chat;
using OlhoNoChat.Inicio;
using OlhoNoChat.Janelas.Chat;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaJanelaChatTestes
{
    [Fact]
    public void ComBordas_ClicavelComMargemEFaixaDoCanal()
    {
        var logica = new LogicaJanelaChat();

        Assert.True(logica.BordasVisiveis);
        Assert.True(logica.Clicavel);
        Assert.True(logica.PaginaRecebeFoco);
        Assert.True(logica.LinksClicaveis);
        Assert.False(logica.MolduraLaranja);
        Assert.Equal(6, logica.MargemDoChat);
        Assert.True(logica.CantoLivre);
        Assert.True(logica.FaixaDoCanalNaTela(TipoDeChat.Padrao));
        Assert.True(logica.FaixaDoCanalNaTela(TipoDeChat.ChatOficial));
        Assert.False(logica.FaixaDoCanalNaTela(TipoDeChat.EnderecoPersonalizado));
        Assert.Equal((true, false), logica.RolagemDaPagina);
    }

    [Fact]
    public void BordasOcultas_CliqueAtravessaSemMargemSemFaixaSemLinks()
    {
        var logica = new LogicaJanelaChat();
        logica.OcultarBordas();

        Assert.False(logica.Clicavel);
        Assert.False(logica.PaginaRecebeFoco);
        Assert.False(logica.LinksClicaveis);
        Assert.Equal(0, logica.MargemDoChat);
        Assert.False(logica.CantoLivre);
        Assert.False(logica.FaixaDoCanalNaTela(TipoDeChat.Padrao));
        Assert.Equal((false, false), logica.RolagemDaPagina);
    }

    [Fact]
    public void ModoRolagem_ComBordasVisiveisNaoFazNada_Decisao5()
    {
        var logica = new LogicaJanelaChat();

        Assert.False(logica.AlternarModoRolagem());
        Assert.False(logica.ModoRolagem);
        Assert.True(logica.Clicavel);
    }

    [Fact]
    public void ModoRolagem_ComBordasOcultasDeixaClicavelComMolduraEAviso()
    {
        var logica = new LogicaJanelaChat();
        logica.OcultarBordas();

        Assert.True(logica.AlternarModoRolagem());
        Assert.True(logica.Clicavel);
        Assert.True(logica.PaginaRecebeFoco);
        Assert.True(logica.MolduraLaranja);
        Assert.False(logica.LinksClicaveis); // links só com as bordas
        Assert.Equal((true, true), logica.RolagemDaPagina);

        Assert.True(logica.AlternarModoRolagem());
        Assert.False(logica.Clicavel);
        Assert.False(logica.MolduraLaranja);
    }

    [Fact]
    public void OcultarEMostrarBordas_SaemDoModoRolagem()
    {
        var logica = new LogicaJanelaChat();
        logica.OcultarBordas();
        logica.AlternarModoRolagem();

        logica.MostrarBordas();
        Assert.False(logica.ModoRolagem);
        logica.OcultarBordas();
        Assert.False(logica.ModoRolagem);
        Assert.False(logica.Clicavel);
    }

    [Fact]
    public void CliqueNoAvisoDoModoRolagem_Sai()
    {
        var logica = new LogicaJanelaChat();
        Assert.False(logica.SairDoModoRolagem());
        logica.OcultarBordas();
        logica.AlternarModoRolagem();

        Assert.True(logica.SairDoModoRolagem());
        Assert.False(logica.Clicavel);
    }

    [Fact]
    public void EscrevendoPeloAtalhoComBordasOcultas_ClicavelMasAPaginaNaoRecebeFoco()
    {
        var logica = new LogicaJanelaChat();
        logica.OcultarBordas();
        logica.Escrevendo = true;

        Assert.True(logica.Clicavel);
        Assert.False(logica.PaginaRecebeFoco);
        Assert.False(logica.MolduraLaranja);

        logica.EscrevendoNaCaixaDaTwitch = true;
        Assert.True(logica.PaginaRecebeFoco);
    }

    [Fact]
    public void CaixaDoAppNaTela_OChatDeixaALinhaDelaEOCantoNaoFicaLivre()
    {
        var logica = new LogicaJanelaChat();
        Assert.Equal(2, logica.LinhasDoChat);

        logica.CaixaDoAppNaTela = true;
        Assert.Equal(1, logica.LinhasDoChat);
        Assert.False(logica.CantoLivre);
    }

    [Theory]
    [InlineData(165, true, false, 165 / 255.0)]
    [InlineData(0, true, false, 0.01)]    // 0% com a janela clicável vira 1%
    [InlineData(0, false, false, 0.0)]    // clique atravessa: 0 de verdade
    [InlineData(0, false, true, 0.01)]    // modo rolagem
    [InlineData(255, false, false, 1.0)]
    public void OpacidadeDoFundo(byte fundo, bool bordas, bool rolagem, double esperado)
    {
        var logica = new LogicaJanelaChat();
        if (!bordas)
            logica.OcultarBordas();
        if (rolagem)
            logica.AlternarModoRolagem();

        Assert.Equal(esperado, logica.OpacidadeDoFundo(fundo), 6);
    }

    [Fact]
    public void BarraDeTarefas_SoSomeComBordasOcultasEAOpcaoLigada()
    {
        var logica = new LogicaJanelaChat();
        Assert.True(logica.NaBarraDeTarefas(esconderIcone: true));

        logica.OcultarBordas();
        Assert.True(logica.NaBarraDeTarefas(esconderIcone: false));
        Assert.False(logica.NaBarraDeTarefas(esconderIcone: true));
    }

    [Fact]
    public void Comandos_EsperamAJanelaFicarPronta_Decisao9()
    {
        var logica = new LogicaJanelaChat();

        Assert.Empty(logica.Receber([ComandoDoApp.AbrirConfiguracoes]));
        Assert.Empty(logica.Receber([ComandoDoApp.AlternarBordas, ComandoDoApp.MostrarJanela]));
        Assert.False(logica.Pronta);

        Assert.Equal([ComandoDoApp.AbrirConfiguracoes, ComandoDoApp.AlternarBordas, ComandoDoApp.MostrarJanela], logica.FicouPronta());
        Assert.True(logica.Pronta);

        // Depois de pronta: na hora, e nada mais fica guardado
        Assert.Equal([ComandoDoApp.RestaurarPosicao], logica.Receber([ComandoDoApp.RestaurarPosicao]));
        Assert.Empty(logica.FicouPronta());
    }
}
