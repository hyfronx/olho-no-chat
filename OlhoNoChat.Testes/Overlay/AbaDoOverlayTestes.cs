using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Testes.Overlay;

/// <summary>
/// A arrumação da aba Overlay: a lista "Quantas mensagens" (que mostra o "Preencher a área toda" gravado) e a dica dos
/// botões de teste, limpar e abrir quando estão desligados.
/// </summary>
public sealed class AbaDoOverlayTestes
{
    private static (LogicaOverlay Aba, Action<EstadoDoOverlay> Mudar) AbaCom(EstadoDoOverlay inicial = EstadoDoOverlay.Funcionando)
    {
        EstadoDoOverlay estado = inicial;
        var aba = new LogicaOverlay(new AcoesDoOverlay(() => estado, () => 1, () => { }, () => { }, () => null));
        return (aba, novo => estado = novo);
    }

    [Fact]
    public void Quantidades_AsQueCouberemEAteUmNumero()
    {
        Assert.Equal(["As que couberem", "Até um número"], LogicaOverlay.Quantidades.Select(q => q.Nome));
    }

    [Fact]
    public void Quantidade_SegueOPreencherAreaGravado()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());
        Assert.Equal(LogicaOverlay.Quantidades[0].Valor, aba.Quantidade);
        Assert.False(aba.ComLimiteDeMensagens);

        aba.Quantidade = LogicaOverlay.Quantidades[1].Valor;
        var gravadas = new Opcoes();
        aba.Gravar(gravadas);

        Assert.False(aba.PreencherArea);
        Assert.True(aba.ComLimiteDeMensagens);
        Assert.False(gravadas.PreencherAreaNoOverlay);
    }

    [Fact]
    public void Quantidade_CarregaAteUmNumeroQuandoNaoPreenche()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes { PreencherAreaNoOverlay = false });

        Assert.Equal(LogicaOverlay.Quantidades[1].Valor, aba.Quantidade);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("outro")]
    public void Quantidade_ValorForaDaListaNaoMuda(string? valor)
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());

        aba.Quantidade = valor!;

        Assert.True(aba.PreencherArea);
    }

    [Fact]
    public void Quantidade_AvisaATelaQuandoOPreencherMuda()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());
        var avisadas = new List<string?>();
        aba.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);

        aba.PreencherArea = false;

        Assert.Contains(nameof(LogicaOverlay.Quantidade), avisadas);
        Assert.Contains(nameof(LogicaOverlay.ComLimiteDeMensagens), avisadas);
    }

    [Fact]
    public void Quantidade_MudarEVoltarNaoContaComoMudanca()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());
        var antes = new Dictionary<string, string>();
        aba.Estado(antes);

        aba.Quantidade = LogicaOverlay.Quantidades[1].Valor;
        var mudado = new Dictionary<string, string>();
        aba.Estado(mudado);
        aba.Quantidade = LogicaOverlay.Quantidades[0].Valor;
        var depois = new Dictionary<string, string>();
        aba.Estado(depois);

        Assert.NotEqual(antes, mudado);
        Assert.Equal(antes, depois);
    }

    [Theory]
    [InlineData(1, "1 mensagem")]
    [InlineData(12, "12 mensagens")]
    public void TextoDasMensagens_ComONome(double mensagens, string texto)
    {
        var aba = new LogicaOverlay { Mensagens = mensagens };

        Assert.Equal(texto, aba.TextoDasMensagens);
    }

    [Fact]
    public void FaltaParaUsar_DizOQueFaltaAteFuncionar()
    {
        var (aba, mudar) = AbaCom(EstadoDoOverlay.Desligado);
        aba.Carregar(new Opcoes());
        Assert.Equal("Ligue o overlay e clique em Salvar.", aba.FaltaParaUsar);

        aba.OverlayLigado = true;
        Assert.Equal("Clique em Salvar para ligar o overlay.", aba.FaltaParaUsar);

        aba.Gravar(new Opcoes());
        Assert.Equal("O overlay está abrindo. Espere um pouco.", aba.FaltaParaUsar);

        mudar(EstadoDoOverlay.PortaOcupada);
        aba.AtualizarEstado();
        Assert.Equal("O endereço está ocupado. Veja como resolver acima.", aba.FaltaParaUsar);

        mudar(EstadoDoOverlay.Funcionando);
        aba.AtualizarEstado();
        Assert.True(aba.Funcionando);
        Assert.Null(aba.FaltaParaUsar);

        aba.OverlayLigado = false;
        Assert.Equal("Ligue o overlay de novo.", aba.FaltaParaUsar);
    }

    [Fact]
    public void FaltaParaUsar_AvisaATelaComOEstado()
    {
        var (aba, _) = AbaCom();
        aba.Carregar(new Opcoes());
        var avisadas = new List<string?>();
        aba.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);

        aba.OverlayLigado = true;
        Assert.Contains(nameof(LogicaOverlay.FaltaParaUsar), avisadas);

        avisadas.Clear();
        aba.AtualizarEstado();
        Assert.Contains(nameof(LogicaOverlay.FaltaParaUsar), avisadas);
    }
}
