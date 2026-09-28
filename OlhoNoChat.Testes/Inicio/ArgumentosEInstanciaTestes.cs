using OlhoNoChat.Inicio;

namespace OlhoNoChat.Testes.Inicio;

public class ArgumentosEInstanciaTestes
{
    [Fact]
    public void Argumentos_SaoLidosNaOrdemSemDiferenciarMaiusculas()
    {
        var comandos = ArgumentosDoApp.Ler(["/Settings", "/TOGGLEBORDERS", "/resetwindow"]);
        Assert.Equal([ComandoDoApp.AbrirConfiguracoes, ComandoDoApp.AlternarBordas, ComandoDoApp.RestaurarPosicao], comandos);
    }

    [Fact]
    public void Argumentos_DesconhecidosSaoIgnorados()
    {
        Assert.Empty(ArgumentosDoApp.Ler(["/qualquer", "texto", ""]));
        Assert.Equal([ComandoDoApp.AlternarBordas], ArgumentosDoApp.Ler(["--x", "/toggleborders"]));
    }

    [Fact]
    public void SemArgumentos_NenhumComando()
    {
        Assert.Empty(ArgumentosDoApp.Ler([]));
    }

    // O formato das mensagens entre cópias é o mesmo desde a 1.0: uma cópia nova conversa com uma já aberta
    [Fact]
    public void Mensagem_SemArgumentosPedeParaMostrarAJanela()
    {
        Assert.Equal("::SHOW_WINDOW_COMMAND::", InstanciaUnica.MontarMensagem([]));
        Assert.Equal([ComandoDoApp.MostrarJanela], InstanciaUnica.LerMensagem("::SHOW_WINDOW_COMMAND::"));
    }

    [Fact]
    public void Mensagem_ArgumentosSeparadosPorTresBarras()
    {
        string mensagem = InstanciaUnica.MontarMensagem(["/settings", "/toggleborders"]);
        Assert.Equal("/settings|||/toggleborders", mensagem);
        Assert.Equal([ComandoDoApp.AbrirConfiguracoes, ComandoDoApp.AlternarBordas], InstanciaUnica.LerMensagem(mensagem));
    }

    [Fact]
    public void Mensagem_SoComArgumentoDesconhecidoNaoFazNada()
    {
        Assert.Empty(InstanciaUnica.LerMensagem("/desconhecido"));
    }
}
