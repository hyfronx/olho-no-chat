using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Filtros;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>O aviso de nome que não vale nos Filtros do chat: o dos filtros, que fala de todas as plataformas.</summary>
public class LogicaFiltrosNomeInvalidoTestes
{
    private static LogicaFiltros Nova() => new(new Opcoes(), () => true);

    [Fact]
    public void DicaNomeInvalido_TextoDaTela()
    {
        Assert.Equal(
            "Use o nome do endereço do canal (twitch.tv/nome) ou o @ da pessoa no YouTube, no TikTok ou na Kick: só letras, números, _, ponto e traço.",
            LogicaFiltros.DicaNomeInvalido);
    }

    [Theory]
    [InlineData("nome errado!")]
    [InlineData("Nome Com Espaço")] // o nome que aparece no YouTube, em vez do @
    [InlineData("çedilha")]
    [InlineData("@a-")] // @ do YouTube curto demais
    public void NomeInvalido_NaListaDeUsuariosMostraADicaDosFiltros(string digitado)
    {
        var logica = Nova();
        logica.NovoUsuario = digitado;

        logica.AdicionarUsuarioCommand.Execute(null);

        Assert.Empty(logica.ListaDeUsuarios);
        Assert.Equal(LogicaFiltros.DicaNomeInvalido, logica.ErroNovoUsuario);
    }

    [Theory]
    [InlineData("nome errado!")]
    [InlineData("Nome Com Espaço")]
    public void NomeInvalido_NosBloqueiosMostraADicaDosFiltros(string digitado)
    {
        var logica = Nova();
        logica.NovoBloqueado = digitado;

        logica.BloquearCommand.Execute(null);

        Assert.Empty(logica.UsuariosBloqueados);
        Assert.Equal(LogicaFiltros.DicaNomeInvalido, logica.ErroNovoBloqueado);
    }
}
