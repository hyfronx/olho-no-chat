using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>
/// A aba Chat sem a tela: o deslizante "Apagar depois de" só dá tempos que valem, um texto salvo que não vale aparece
/// como o padrão, um tempo salvo fora da lista entra no deslizante e o número é gravado como texto.
/// </summary>
public sealed class LogicaChatTestes
{
    private static readonly int Padrao = int.Parse(new Opcoes().SegundosParaApagar);

    private static LogicaChat Abrir(string segundosSalvos)
    {
        var chat = new LogicaChat(() => new Opcoes(), () => true);
        chat.Carregar(new Opcoes { SegundosParaApagar = segundosSalvos });
        return chat;
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("1,5")]
    [InlineData("99999999999")]
    public void ApagarDepoisDe_TextoSalvoQueNaoValeApareceComoOPadrao(string salvo)
    {
        LogicaChat chat = Abrir(salvo);

        Assert.Equal(Padrao, chat.SegundosParaApagar);
        Assert.Equal("2 minutos", chat.TextoParaApagar);
        Assert.Equal(LogicaChat.TemposParaApagar.Count - 1, chat.UltimaPosicaoParaApagar);
        Assert.Equal(Padrao, LogicaChat.TemposParaApagar[chat.PosicaoParaApagar]);
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData(" 45 ", 45)]
    [InlineData("600", 600)]
    public void ApagarDepoisDe_TempoSalvoDaListaAparece(string salvo, int segundos)
    {
        LogicaChat chat = Abrir(salvo);

        Assert.Equal(segundos, chat.SegundosParaApagar);
        Assert.Equal(LogicaChat.TemposParaApagar.Count - 1, chat.UltimaPosicaoParaApagar);
        Assert.Equal(segundos, LogicaChat.TemposParaApagar[chat.PosicaoParaApagar]);
    }

    [Fact]
    public void ApagarDepoisDe_DeslizanteSoDaOsTemposDaLista()
    {
        LogicaChat chat = Abrir("120");

        for (int posicao = 0; posicao <= chat.UltimaPosicaoParaApagar; posicao++)
        {
            chat.PosicaoParaApagar = posicao;
            Assert.Equal(LogicaChat.TemposParaApagar[posicao], chat.SegundosParaApagar);
            Assert.Equal(posicao, chat.PosicaoParaApagar);
        }

        // Fora do deslizante, fica na ponta
        chat.PosicaoParaApagar = -1;
        Assert.Equal(LogicaChat.TemposParaApagar[0], chat.SegundosParaApagar);
        chat.PosicaoParaApagar = 99;
        Assert.Equal(LogicaChat.TemposParaApagar.Last(), chat.SegundosParaApagar);
    }

    [Fact]
    public void ApagarDepoisDe_VaiDe10SegundosA10Minutos()
    {
        Assert.Equal(10, LogicaChat.TemposParaApagar[0]);
        Assert.Equal(600, LogicaChat.TemposParaApagar.Last());
        Assert.Equal(LogicaChat.TemposParaApagar.Order(), LogicaChat.TemposParaApagar);
        Assert.Contains(Padrao, LogicaChat.TemposParaApagar);
    }

    [Fact]
    public void ApagarDepoisDe_TempoSalvoForaDaListaEntraNoDeslizanteEFica()
    {
        LogicaChat chat = Abrir("100");

        Assert.Equal(100, chat.SegundosParaApagar);
        Assert.Equal("1 min 40 s", chat.TextoParaApagar);
        Assert.Equal(LogicaChat.TemposParaApagar.Count, chat.UltimaPosicaoParaApagar);
        int posicaoDoSalvo = chat.PosicaoParaApagar;
        Assert.Equal(LogicaChat.TemposParaApagar.Count(t => t < 100), posicaoDoSalvo);

        // Mudar e voltar: o tempo salvo continua no deslizante
        chat.PosicaoParaApagar = 0;
        Assert.Equal(10, chat.SegundosParaApagar);
        chat.PosicaoParaApagar = posicaoDoSalvo;
        Assert.Equal(100, chat.SegundosParaApagar);
    }

    [Fact]
    public void ApagarDepoisDe_CarregarDeNovoTiraOTempoQueTinhaEntrado()
    {
        LogicaChat chat = Abrir("100");

        chat.Carregar(new Opcoes { SegundosParaApagar = "45" });

        Assert.Equal(45, chat.SegundosParaApagar);
        Assert.Equal(LogicaChat.TemposParaApagar.Count - 1, chat.UltimaPosicaoParaApagar);
        Assert.Equal(45, LogicaChat.TemposParaApagar[chat.PosicaoParaApagar]);
    }

    [Fact]
    public void ApagarDepoisDe_OMaximoDoDeslizanteMudaAntesDaPosicao()
    {
        var chat = new LogicaChat(() => new Opcoes(), () => true);
        var avisos = new List<string?>();
        chat.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        chat.Carregar(new Opcoes { SegundosParaApagar = "100" });
        Assert.Contains(nameof(LogicaChat.UltimaPosicaoParaApagar), avisos);
        Assert.True(avisos.IndexOf(nameof(LogicaChat.UltimaPosicaoParaApagar)) < avisos.IndexOf(nameof(LogicaChat.PosicaoParaApagar)));

        // Um tempo fora do deslizante posto pelo código também entra nele antes
        avisos.Clear();
        chat.SegundosParaApagar = 99;
        Assert.Contains(nameof(LogicaChat.UltimaPosicaoParaApagar), avisos);
        Assert.True(avisos.IndexOf(nameof(LogicaChat.UltimaPosicaoParaApagar)) < avisos.IndexOf(nameof(LogicaChat.PosicaoParaApagar)));
        Assert.Equal(99, chat.SegundosParaApagar);
        Assert.Equal(99, LogicaChat.TemposParaApagar.Append(100).Append(99).Order().ElementAt(chat.PosicaoParaApagar));
    }

    [Fact]
    public void ApagarDepoisDe_GravaONumeroComoTexto()
    {
        LogicaChat chat = Abrir("abc");
        chat.PosicaoParaApagar = 0;
        var opcoes = new Opcoes { SegundosParaApagar = "abc" };

        chat.Gravar(opcoes);

        Assert.Equal("10", opcoes.SegundosParaApagar);
    }

    [Fact]
    public void ApagarDepoisDe_MudarEVoltarDaOMesmoEstado()
    {
        LogicaChat chat = Abrir("45");
        var antes = new Dictionary<string, string>();
        chat.Estado(antes);

        chat.PosicaoParaApagar = chat.UltimaPosicaoParaApagar;
        var mudado = new Dictionary<string, string>();
        chat.Estado(mudado);
        Assert.Equal("600", mudado["Chat.SegundosParaApagar"]);

        chat.SegundosParaApagar = 45;
        var depois = new Dictionary<string, string>();
        chat.Estado(depois);
        Assert.Equal(antes, depois);
    }

    [Theory]
    [InlineData(1, "1 segundo")]
    [InlineData(10, "10 segundos")]
    [InlineData(45, "45 segundos")]
    [InlineData(60, "1 minuto")]
    [InlineData(90, "1 min 30 s")]
    [InlineData(120, "2 minutos")]
    [InlineData(600, "10 minutos")]
    public void TextoDoTempo_EmSegundosOuMinutos(int segundos, string texto)
    {
        Assert.Equal(texto, LogicaChat.TextoDoTempo(segundos));
    }
}
