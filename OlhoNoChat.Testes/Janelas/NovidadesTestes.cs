using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>O balão da novidade da pausa: uma vez só, e só quando o botão de pausar está na tela.</summary>
public class NovidadesTestes
{
    [Fact]
    public void ChatPadraoComBordas_AindaNaoVista_Aparece()
    {
        Assert.True(Novidades.PodeMostrarAPausa([], chatPadraoCarregado: true, bordasVisiveis: true, faixaAberta: false,
            configuracoesAbertas: false));
    }

    [Fact]
    public void JaVista_NuncaMais()
    {
        Assert.False(Novidades.PodeMostrarAPausa([Novidades.Pausa], true, true, false, false));
    }

    [Theory]
    [InlineData(false, true, false, false)] // outro tipo de chat, boas-vindas ou a página ainda carregando
    [InlineData(true, false, false, false)] // bordas ocultas: a barra com o botão não está na tela
    [InlineData(true, true, true, false)]   // faixa do canal aberta: o balão cobriria as caixas
    [InlineData(true, true, false, true)]   // Configurações abertas
    public void SemOBotaoNaTela_Espera(bool padrao, bool bordas, bool faixa, bool configuracoes)
    {
        Assert.False(Novidades.PodeMostrarAPausa([], padrao, bordas, faixa, configuracoes));
    }

    [Fact]
    public void RestaurarTudo_NaoMostraDeNovo()
    {
        var opcoes = new Opcoes { NovidadesVistas = [Novidades.Pausa] };

        Assert.Equal([Novidades.Pausa], opcoes.RestauradasParaOPadrao().NovidadesVistas);
    }

    [Fact]
    public void ArquivoSemALista_ComecaVazia()
    {
        var opcoes = new Opcoes { NovidadesVistas = null! };

        opcoes.Corrigir();

        Assert.Empty(opcoes.NovidadesVistas);
    }
}
