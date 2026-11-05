using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>Os balões das novidades: uma vez só cada, todas as que faltam em sequência, e só quando o botão delas está na tela.</summary>
public class NovidadesTestes
{
    private static string? Proxima(IReadOnlyCollection<string> vistas, bool autoMod = true,
        bool padrao = true, bool bordas = true, bool faixa = false, bool configuracoes = false) =>
        Novidades.Proxima(vistas, autoMod, padrao, bordas, faixa, configuracoes);

    [Fact]
    public void NenhumaVista_APausaPrimeiro()
    {
        Assert.Equal(Novidades.Pausa, Proxima([]));
    }

    [Fact]
    public void PausaVista_DepoisOAutoMod()
    {
        Assert.Equal(Novidades.AutoMod, Proxima([Novidades.Pausa]));
    }

    [Fact]
    public void AutoMod_EsperaAOpcaoEAConferenciaDaConta()
    {
        // Opção desligada, ou a conta conectada ainda sem as permissões conferidas
        Assert.Null(Proxima([Novidades.Pausa], autoMod: false));
        // A da pausa não depende disso
        Assert.Equal(Novidades.Pausa, Proxima([], autoMod: false));
    }

    [Fact]
    public void TodasVistas_NuncaMais()
    {
        Assert.Null(Proxima([Novidades.Pausa, Novidades.AutoMod]));
    }

    [Fact]
    public void QuemSoViuADoAutoMod_AindaVeAPausa()
    {
        Assert.Equal(Novidades.Pausa, Proxima([Novidades.AutoMod]));
    }

    [Theory]
    [InlineData(false, true, false, false)] // outro tipo de chat, boas-vindas ou a página ainda carregando
    [InlineData(true, false, false, false)] // bordas ocultas: a barra com o botão não está na tela
    [InlineData(true, true, true, false)]   // faixa do canal aberta: o balão cobriria as caixas
    [InlineData(true, true, false, true)]   // Configurações abertas
    public void SemOBotaoNaTela_Espera(bool padrao, bool bordas, bool faixa, bool configuracoes)
    {
        Assert.Null(Proxima([], padrao: padrao, bordas: bordas, faixa: faixa, configuracoes: configuracoes));
        Assert.Null(Proxima([Novidades.Pausa], padrao: padrao, bordas: bordas, faixa: faixa, configuracoes: configuracoes));
    }

    [Fact]
    public void RestaurarTudo_NaoMostraDeNovo()
    {
        var opcoes = new Opcoes { NovidadesVistas = [Novidades.Pausa, Novidades.AutoMod] };

        Assert.Equal([Novidades.Pausa, Novidades.AutoMod], opcoes.RestauradasParaOPadrao().NovidadesVistas);
    }

    [Fact]
    public void ArquivoSemALista_ComecaVazia()
    {
        var opcoes = new Opcoes { NovidadesVistas = null! };

        opcoes.Corrigir();

        Assert.Empty(opcoes.NovidadesVistas);
    }
}
