using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>
/// A aba Aparência sozinha: o aviso de que trocar o tema recarrega o chat (só com um tema diferente do salvo) e a cor,
/// o contorno e a fonte, que não valem no chat oficial com o CSS próprio.
/// </summary>
public sealed class LogicaAparenciaTestes
{
    private static LogicaAparencia Abrir(int temaSalvo = Opcoes.TemaPadrao)
    {
        var aparencia = new LogicaAparencia { TipoNaTela = TipoDeChat.Padrao };
        aparencia.Carregar(new Opcoes { Tema = temaSalvo });
        return aparencia;
    }

    // ----- Aviso de que o chat recarrega -----

    [Fact]
    public void TemaIgualAoSalvo_NaoAvisa()
    {
        Assert.False(Abrir().TemaRecarregaOChat);
        Assert.False(Abrir(Opcoes.TemaNenhum).TemaRecarregaOChat);
    }

    [Fact]
    public void TrocarOTema_Avisa_EVoltarAoSalvoTiraOAviso()
    {
        var aparencia = Abrir();
        var mudancas = new List<string?>();
        aparencia.PropertyChanged += (_, e) => mudancas.Add(e.PropertyName);

        aparencia.Tema = Opcoes.TemaNenhum.ToString();
        Assert.True(aparencia.TemaRecarregaOChat);
        Assert.Contains(nameof(LogicaAparencia.TemaRecarregaOChat), mudancas);

        aparencia.Tema = Opcoes.TemaPadrao.ToString();
        Assert.False(aparencia.TemaRecarregaOChat);
    }

    [Fact]
    public void SalvarNoPadrao_TiraOAviso()
    {
        var aparencia = Abrir();
        aparencia.Tema = Opcoes.TemaNenhum.ToString();

        var opcoes = new Opcoes();
        aparencia.Gravar(opcoes);

        Assert.Equal(Opcoes.TemaNenhum, opcoes.Tema);
        Assert.False(aparencia.TemaRecarregaOChat);
    }

    [Fact]
    public void SalvarComOutroTipo_NaoGravaOTema_EOAvisoVoltaComOPadrao()
    {
        var aparencia = Abrir();
        aparencia.Tema = Opcoes.TemaNenhum.ToString();
        aparencia.TipoNaTela = TipoDeChat.ChatOficial;

        var opcoes = new Opcoes();
        aparencia.Gravar(opcoes);
        aparencia.TipoNaTela = TipoDeChat.Padrao;

        Assert.Equal(Opcoes.TemaPadrao, opcoes.Tema);
        Assert.True(aparencia.TemaRecarregaOChat);
    }

    [Fact]
    public void CarregarDeNovo_TiraOAviso()
    {
        var aparencia = Abrir();
        aparencia.Tema = Opcoes.TemaNenhum.ToString();

        aparencia.Carregar(new Opcoes { Tema = Opcoes.TemaPadrao });

        Assert.False(aparencia.TemaRecarregaOChat);
    }

    // ----- Cor do texto, contorno e fonte -----

    [Theory]
    [InlineData(TipoDeChat.Padrao, true, true)]
    [InlineData(TipoDeChat.Padrao, false, true)]
    [InlineData(TipoDeChat.ChatOficial, true, true)]
    [InlineData(TipoDeChat.ChatOficial, false, false)]
    public void CorContornoEFonte_SoNaoValemNoChatOficialComOCssProprio(TipoDeChat tipo, bool aparenciaPadrao, bool valem)
    {
        var aparencia = new LogicaAparencia { TipoNaTela = tipo, AparenciaPadraoNoChatOficial = aparenciaPadrao };

        Assert.Equal(valem, aparencia.CorContornoEFonteValem);
    }

    [Fact]
    public void CorContornoEFonte_AvisamAoTrocarOTipoEAAparenciaPadrao()
    {
        var aparencia = new LogicaAparencia { TipoNaTela = TipoDeChat.Padrao, AparenciaPadraoNoChatOficial = false };
        var mudancas = new List<string?>();
        aparencia.PropertyChanged += (_, e) => mudancas.Add(e.PropertyName);

        aparencia.TipoNaTela = TipoDeChat.ChatOficial;
        Assert.False(aparencia.CorContornoEFonteValem);
        aparencia.AparenciaPadraoNoChatOficial = true;
        Assert.True(aparencia.CorContornoEFonteValem);

        Assert.Equal(2, mudancas.Count(nome => nome == nameof(LogicaAparencia.CorContornoEFonteValem)));
    }
}
