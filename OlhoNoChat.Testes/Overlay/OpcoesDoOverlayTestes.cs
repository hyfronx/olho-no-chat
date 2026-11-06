using System.Text.Json;
using System.Windows.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Overlay;

namespace OlhoNoChat.Testes.Overlay;

/// <summary>
/// As opções do overlay: desligado na primeira instalação, valores fora dos limites corrigidos, o atalho de limpar sem
/// brigar com um atalho que a pessoa já tem, o "Restaurar" sem mudar o endereço, e a aba Overlay (carregar, gravar,
/// mudanças, o estado e o "Encontrar outro endereço").
/// </summary>
public sealed class OpcoesDoOverlayTestes
{
    private static Opcoes Corrigidas(string json)
    {
        Opcoes opcoes = JsonSerializer.Deserialize<Opcoes>(json, ArquivoDeConfiguracoes.Formato)!;
        opcoes.Corrigir();
        return opcoes;
    }

    [Fact]
    public void PrimeiraInstalacao_DesligadoNaPortaPadrao()
    {
        var opcoes = new Opcoes();

        Assert.False(opcoes.OverlayLigado);
        Assert.Equal(ServidorDoOverlay.PortaPadrao, opcoes.PortaDoOverlay);
        Assert.True(opcoes.OverlaySome);
        Assert.Equal(30, opcoes.SegundosParaSumirNoOverlay);
        Assert.Equal(0, opcoes.SegundosDeAtrasoNoOverlay);
        Assert.Equal("replace", opcoes.LinksNoOverlay);
        Assert.Empty(opcoes.PalavrasProibidasNoOverlay);
        Assert.Equal(new Atalho(Key.F5, ModifierKeys.Control | ModifierKeys.Alt), opcoes.AtalhoLimparOverlay);
        Assert.True(opcoes.PreencherAreaNoOverlay); // ligado por padrão (pedido do usuário, 2026-11-06)
        Assert.Equal("pop", opcoes.AnimacaoDoOverlay); // "Pular", escolha do usuário (2026-11-06)
    }

    // Quem já tinha o app não tem a chave no arquivo: fica com o padrão, ligado
    [Fact]
    public void PreencherArea_ArquivoSemAChaveFicaLigado()
    {
        Assert.True(Corrigidas("""{"MensagensNoOverlay":12}""").PreencherAreaNoOverlay);
        Assert.False(Corrigidas("""{"PreencherAreaNoOverlay":false}""").PreencherAreaNoOverlay);
    }

    [Fact]
    public void Aba_MensagensNaTelaSoValeSemPreencherENaLista()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());

        Assert.True(aba.PreencherArea);
        Assert.False(aba.ComLimiteDeMensagens);
        aba.PreencherArea = false;
        Assert.True(aba.ComLimiteDeMensagens);
        aba.Disposicao = "ticker";
        Assert.False(aba.ComLimiteDeMensagens);
    }

    [Fact]
    public void Corrigir_NumerosForaDosLimitesVoltamParaDentro()
    {
        Opcoes o = Corrigidas("""
            {"SegundosParaSumirNoOverlay":1,"SegundosDeAtrasoNoOverlay":-3,"MensagensNoOverlay":999,"TamanhoDoTextoNoOverlay":2,
             "OpacidadeDoFundoNoOverlay":150,"CaracteresNoOverlay":5,"EmotesNoOverlay":0}
            """);

        Assert.Equal(Opcoes.SegundosParaSumirNoOverlayMinimo, o.SegundosParaSumirNoOverlay);
        Assert.Equal(0, o.SegundosDeAtrasoNoOverlay);
        Assert.Equal(Opcoes.MensagensNoOverlayMaximo, o.MensagensNoOverlay);
        Assert.Equal(Opcoes.TamanhoDoTextoNoOverlayMinimo, o.TamanhoDoTextoNoOverlay);
        Assert.Equal(100, o.OpacidadeDoFundoNoOverlay);
        Assert.Equal(Opcoes.CaracteresNoOverlayMinimo, o.CaracteresNoOverlay);
        Assert.Equal(Opcoes.EmotesNoOverlayMinimo, o.EmotesNoOverlay);
    }

    [Fact]
    public void Corrigir_TextoDesconhecidoVoltaParaOPadrao()
    {
        Opcoes o = Corrigidas("""
            {"FonteDoOverlay":"Comic Sans MS","ContornoNoOverlay":"neon","FundoNoOverlay":null,"DirecaoDoOverlay":"left",
             "AlinhamentoDoOverlay":"center","AnimacaoDoOverlay":"spin","LinksNoOverlay":"",
             "PalavrasProibidasNoOverlay":["feio",null,"  "]}
            """);
        var padrao = new Opcoes();

        Assert.Equal(padrao.FonteDoOverlay, o.FonteDoOverlay);
        Assert.Equal(padrao.ContornoNoOverlay, o.ContornoNoOverlay);
        Assert.Equal(padrao.FundoNoOverlay, o.FundoNoOverlay);
        Assert.Equal(padrao.DirecaoDoOverlay, o.DirecaoDoOverlay);
        Assert.Equal(padrao.AlinhamentoDoOverlay, o.AlinhamentoDoOverlay);
        Assert.Equal(padrao.AnimacaoDoOverlay, o.AnimacaoDoOverlay);
        Assert.Equal(padrao.LinksNoOverlay, o.LinksNoOverlay);
        Assert.Equal(["feio"], o.PalavrasProibidasNoOverlay);
    }

    [Fact]
    public void Corrigir_DisposicaoEVelocidadeDaFaixa()
    {
        Opcoes o = Corrigidas("""{"DisposicaoDoOverlay":"diagonal","VelocidadeDaFaixaNoOverlay":5,"FundoNoOverlay":"bubble"}""");

        Assert.Equal("list", o.DisposicaoDoOverlay);
        Assert.Equal(Opcoes.VelocidadeDaFaixaMinima, o.VelocidadeDaFaixaNoOverlay);
        Assert.Equal("bubble", o.FundoNoOverlay);
        Assert.Equal("ticker", Corrigidas("""{"DisposicaoDoOverlay":"ticker","VelocidadeDaFaixaNoOverlay":9999}""").DisposicaoDoOverlay);
        Assert.Equal(Opcoes.VelocidadeDaFaixaMaxima, Corrigidas("""{"VelocidadeDaFaixaNoOverlay":9999}""").VelocidadeDaFaixaNoOverlay);
    }

    [Fact]
    public void Aba_EscuroValeParaCartaoEBalao_EFaixaEscondeAsOpcoesDaLista()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());

        Assert.False(aba.ComCartao);
        aba.Fundo = "bubble";
        Assert.True(aba.ComCartao);
        Assert.True(aba.EmLista);
        aba.Disposicao = "ticker";
        Assert.False(aba.EmLista);
        Assert.True(aba.EmFaixa);
    }

    [Theory]
    [InlineData("rise")]
    [InlineData("pop")]
    [InlineData("bounce")]
    [InlineData("blur")]
    [InlineData("flip")]
    public void Corrigir_AsAnimacoesNovasContinuam(string animacao)
    {
        Assert.Equal(animacao, Corrigidas($$"""{"AnimacaoDoOverlay":"{{animacao}}"}""").AnimacaoDoOverlay);
    }

    [Fact]
    public void Animacoes_ALista_DaAbaEOQueAPaginaConhece()
    {
        Assert.Equal(ContratoDoOverlay.Animacoes.Order(), LogicaOverlay.Animacoes.Select(a => a.Valor).Order());
        // Cada uma (menos "none") tem a entrada e a saída no CSS da página
        string css = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "browser", "overlay.css"));
        foreach (string animacao in ContratoDoOverlay.Animacoes.Where(a => a != "none"))
        {
            Assert.Contains(".ov-enter-" + animacao + " ", css);
            Assert.Contains(".ov-leave-" + animacao + " ", css);
        }
    }

    [Theory]
    [InlineData(80)]
    [InlineData(0)]
    [InlineData(70000)]
    public void Corrigir_PortaInvalidaVoltaParaAPadrao(int porta)
    {
        Assert.Equal(ServidorDoOverlay.PortaPadrao, Corrigidas($$"""{"PortaDoOverlay":{{porta}}}""").PortaDoOverlay);
    }

    [Fact]
    public void Corrigir_PortaEscolhidaContinua()
    {
        Assert.Equal(28987, Corrigidas("""{"PortaDoOverlay":28987}""").PortaDoOverlay);
    }

    [Fact]
    public void AtalhoDeLimpar_JaUsadoPorOutroFicaSemAtalho()
    {
        Opcoes o = Corrigidas("""{"AtalhoPausar":{"Tecla":94,"Modificadores":3}}""");

        Assert.Equal(new Atalho(Key.F5, ModifierKeys.Control | ModifierKeys.Alt), o.AtalhoPausar);
        Assert.Null(o.AtalhoLimparOverlay);
    }

    [Fact]
    public void AtalhoDeLimpar_SemConflitoFicaComOPadrao()
    {
        Assert.Equal(new Opcoes().AtalhoLimparOverlay, Corrigidas("{}").AtalhoLimparOverlay);
    }

    [Fact]
    public void Restaurar_MantemOEnderecoEAsPalavras()
    {
        var opcoes = new Opcoes
        {
            OverlayLigado = true,
            PortaDoOverlay = 28987,
            PalavrasProibidasNoOverlay = ["feio"],
            MensagensNoOverlay = 3,
        };

        Opcoes restauradas = opcoes.RestauradasParaOPadrao();

        Assert.False(restauradas.OverlayLigado);
        Assert.Equal(28987, restauradas.PortaDoOverlay);
        Assert.Equal(["feio"], restauradas.PalavrasProibidasNoOverlay);
        Assert.Equal(Opcoes.MensagensNoOverlayPadrao, restauradas.MensagensNoOverlay);
    }

    // ----- A aba Overlay -----

    [Fact]
    public void Aba_CarregarEGravarDevolveOMesmo()
    {
        var salvas = new Opcoes
        {
            OverlayLigado = true,
            PortaDoOverlay = 18987,
            AtalhoLimparOverlay = new OlhoNoChat.Atalhos.Atalho(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift),
            SegundosParaSumirNoOverlay = 45,
            SegundosDeAtrasoNoOverlay = 4,
            FonteDoOverlay = "Arial",
            FundoNoOverlay = "bubble",
            DisposicaoDoOverlay = "ticker",
            VelocidadeDaFaixaNoOverlay = 250,
            PreencherAreaNoOverlay = false,
            PrimeiraVezNoOverlay = false,
            PalavrasProibidasNoOverlay = ["feio", "chato*"],
            LinksNoOverlay = "show",
            OutrosAvisosDoTikTokNoOverlay = false,
        };
        var aba = new LogicaOverlay();
        aba.Carregar(salvas);

        var gravadas = new Opcoes();
        aba.Gravar(gravadas);

        Assert.Equal(ContratoDoOverlay.JsonDasOpcoes(salvas), ContratoDoOverlay.JsonDasOpcoes(gravadas));
        Assert.True(gravadas.OverlayLigado);
        Assert.Equal(18987, gravadas.PortaDoOverlay);
        Assert.Equal(salvas.AtalhoLimparOverlay, gravadas.AtalhoLimparOverlay); // o atalho de limpar fica na aba Overlay
    }

    [Fact]
    public void Aba_AtalhoApagadoFicaSemAtalho()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());
        aba.AtalhoLimpar = null;
        var opcoes = new Opcoes();

        aba.Gravar(opcoes);

        Assert.Null(opcoes.AtalhoLimparOverlay);
    }

    [Fact]
    public void Aba_MudarEVoltarNaoContaComoMudanca()
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes());
        var antes = new Dictionary<string, string>();
        aba.Estado(antes);

        aba.SegundosParaSumir = 90;
        aba.PalavrasProibidas = "feio";
        var mudado = new Dictionary<string, string>();
        aba.Estado(mudado);
        aba.SegundosParaSumir = 30;
        aba.PalavrasProibidas = "";
        var depois = new Dictionary<string, string>();
        aba.Estado(depois);

        Assert.NotEqual(antes, mudado);
        Assert.Equal(antes, depois);
    }

    [Theory]
    [InlineData("feio\nchato", new[] { "feio", "chato" })]
    [InlineData("feio, chato ; bobo", new[] { "feio", "chato", "bobo" })]
    [InlineData("Feio\r\nfeio\n\n  ", new[] { "Feio" })]
    [InlineData("", new string[0])]
    public void Aba_PalavrasUmaPorLinhaOuPorVirgula(string texto, string[] palavras)
    {
        Assert.Equal(palavras, LogicaOverlay.LerPalavras(texto));
    }

    [Theory]
    [InlineData("80")]
    [InlineData("abc")]
    [InlineData("")]
    public void Aba_PortaInvalidaNaoMudaAGravada(string porta)
    {
        var aba = new LogicaOverlay();
        aba.Carregar(new Opcoes { PortaDoOverlay = 18987 });
        aba.Porta = porta;
        var opcoes = new Opcoes { PortaDoOverlay = 18987 };

        aba.Gravar(opcoes);

        Assert.Equal(18987, opcoes.PortaDoOverlay);
    }

    private static (LogicaOverlay Aba, Func<EstadoDoOverlay, EstadoDoOverlay> Mudar) AbaCom(int? portaNova = null, int conectados = 0)
    {
        EstadoDoOverlay estado = EstadoDoOverlay.Funcionando;
        var aba = new LogicaOverlay(new AcoesDoOverlay(() => estado, () => conectados, () => { }, () => { }, () => portaNova));
        return (aba, novo => estado = novo);
    }

    [Fact]
    public void Aba_EstadoSegueOSalvoEOServidor()
    {
        var (aba, mudar) = AbaCom(conectados: 1);
        aba.Carregar(new Opcoes());
        Assert.Equal("Desligado.", aba.TextoDoEstado);

        aba.OverlayLigado = true;
        Assert.Equal("Clique em Salvar para ligar.", aba.TextoDoEstado);
        Assert.False(aba.Funcionando);

        aba.Gravar(new Opcoes());
        Assert.True(aba.Funcionando);
        Assert.Equal("Funcionando. O OBS está conectado.", aba.TextoDoEstado);

        mudar(EstadoDoOverlay.PortaOcupada);
        aba.AtualizarEstado();
        Assert.True(aba.TemProblema);
        Assert.StartsWith("Outro programa", aba.TextoDoEstado);

        mudar(EstadoDoOverlay.OutroOlhoNoChat);
        aba.AtualizarEstado();
        Assert.StartsWith("Outro Olho no Chat", aba.TextoDoEstado);
    }

    [Fact]
    public void Aba_EncontrarOutroEnderecoMostraONovoEOsPassos()
    {
        var (aba, _) = AbaCom(portaNova: 28987);
        aba.Carregar(new Opcoes { OverlayLigado = true });
        int gravadas = 0;
        aba.PortaGravada += () => gravadas++;

        aba.EncontrarOutroEnderecoCommand.Execute(null);

        Assert.Equal("http://localhost:28987/overlay", aba.Endereco);
        Assert.Equal("28987", aba.Porta);
        Assert.True(aba.MostrarPassosDoObs);
        Assert.Equal(1, gravadas);
    }

    [Fact]
    public void Aba_EncontrarOutroEnderecoSemNenhumLivreAvisa()
    {
        var (aba, _) = AbaCom(portaNova: null);
        aba.Carregar(new Opcoes { OverlayLigado = true });

        aba.EncontrarOutroEnderecoCommand.Execute(null);

        Assert.Equal(ServidorDoOverlay.EnderecoDa(ServidorDoOverlay.PortaPadrao), aba.Endereco);
        Assert.False(aba.MostrarPassosDoObs);
        Assert.StartsWith("Nenhum endereço livre", aba.ResultadoDaProcura);
    }
}
