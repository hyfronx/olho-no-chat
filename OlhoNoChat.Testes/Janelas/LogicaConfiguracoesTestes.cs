using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Configuracoes;
using OlhoNoChat.Som;
using OlhoNoChat.Testes.Twitch;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Janelas;

/// <summary>
/// A janela Configurações sem a tela: o que cada tipo de chat grava, "Restaurar tudo para o padrão", a conferência de
/// mudanças (mudar e voltar não conta), as dicas que citam o atalho, o som que sumiu da pasta (decisão 3) e o CSS do
/// chat oficial (decisão 11). As opções ficam num Configuracoes.json de uma pasta de mentira.
/// </summary>
public sealed class LogicaConfiguracoesTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));
    private readonly string _pastaDosSons;
    private readonly List<(string? Arquivo, float Volume, int Saida)> _previas = [];
    private ArquivoDeConfiguracoes _arquivo = null!;

    public LogicaConfiguracoesTestes()
    {
        Directory.CreateDirectory(_pasta);
        _pastaDosSons = Path.Combine(_pasta, "sons");
        Directory.CreateDirectory(_pastaDosSons);
        foreach (string som in new[] { "alfa.wav", "beta-som.wav", "gama.mp3" })
            File.WriteAllBytes(Path.Combine(_pastaDosSons, som), []);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // Abre a pasta, deixa o teste mudar as opções salvas e monta a janela sem a tela
    private LogicaConfiguracoes Abrir(Action<Opcoes>? salvas = null)
    {
        _arquivo = ArquivoDeConfiguracoes.Abrir(_pasta);
        _arquivo.ComecarAGravar();
        salvas?.Invoke(_arquivo.Opcoes);
        _arquivo.Gravar();

        var conta = Montar.Conta(new TwitchFalsa(), new ContaSalvaNaMemoria());
        var twitch = new LogicaTwitch(new AutorizacaoNoNavegador(NullLogger<AutorizacaoNoNavegador>.Instance), conta,
            new ResgatesDePontos(new ApiDaTwitch(new TwitchFalsa()), conta, NullLogger<ResgatesDePontos>.Instance));
        return new LogicaConfiguracoes(_arquivo, twitch, (arquivo, volume, saida) => _previas.Add((arquivo, volume, saida)));
    }

    // O que está gravado no arquivo agora
    private Opcoes Gravadas() => ArquivoDeConfiguracoes.Abrir(_pasta).Opcoes;

    // Muda tudo o que aparece nas abas Chat, Aparência e Som (de todos os tipos) para valores diferentes do padrão
    private void MudarTudoNaTela(LogicaConfiguracoes c)
    {
        c.Chat.ApagarMensagensAntigas = true;
        c.Chat.SegundosParaApagar = "45";
        c.Chat.EsconderBots = false;
        c.Chat.EsconderGifs = true;
        c.Chat.EsconderOutrosCanais = true;
        c.Chat.BetterTtv = false;
        c.Chat.MenuDeEmotesDoBetterTtv = false;
        c.Chat.Emotes7tv = false;
        c.Chat.FrankerFaceZ = false;
        c.Chat.EnderecoPersonalizado = "https://exemplo.invalid/chat";
        c.Aparencia.Tema = "0";
        c.Aparencia.CssDoTemaNenhum = "#chat_box { color: red; }";
        c.Aparencia.CorDoTexto = "#A8DCFF";
        c.Aparencia.ContornoDasLetras = "soft";
        c.Aparencia.Fonte = "Arial";
        c.Aparencia.MostrarHorario = true;
        c.Aparencia.AparenciaPadraoNoChatOficial = false;
        c.Aparencia.TextoDoEditorDoChatOficial = ".chat-line__message { color: blue; }";
        c.Aparencia.CssDoEnderecoPersonalizado = "body { color: green; }";
        c.Som.MudarPasta(_pastaDosSons);
        c.Som.SomEscolhido = "gama.mp3";
        c.Som.QuandoTocar = "30";
        c.Som.Volume = 40;
    }

    // ----- Mudanças -----

    [Fact]
    public void Abrir_NadaMudou()
    {
        var c = Abrir(o => { o.TipoDeChat = 1; o.Volume = 0.37f; o.SegundosEntreSons = 45; o.CorDoTexto = "#123456"; });

        Assert.False(c.TemMudancas);
    }

    [Fact]
    public void MudarEVoltar_NaoContaComoMudanca()
    {
        var c = Abrir();

        c.Chat.TipoDeChat = 2;
        c.Chat.EsconderBots = false;
        c.Chat.SegundosParaApagar = "99";
        c.Aparencia.Fonte = "Verdana";
        c.Som.Volume = 55;
        c.Geral.AtalhoBordas = null;
        c.Twitch.CaixaDeDigitar = 1;
        Assert.True(c.TemMudancas);

        c.Chat.TipoDeChat = 0;
        c.Chat.EsconderBots = true;
        c.Chat.SegundosParaApagar = "120";
        c.Aparencia.Fonte = "theme";
        c.Som.Volume = 100.3; // o volume conta arredondado
        c.Geral.AtalhoBordas = new Atalho(Key.F9, ModifierKeys.Control | ModifierKeys.Alt);
        c.Twitch.CaixaDeDigitar = 0;
        Assert.False(c.TemMudancas);
    }

    [Fact]
    public void MudancaEscondidaPeloTipo_TambemConta()
    {
        var c = Abrir();

        c.Chat.FrankerFaceZ = false; // só aparece no chat oficial

        Assert.True(c.TemMudancas);
    }

    [Fact]
    public void Salvar_TiraAsMudancasEAvisaOChat()
    {
        var c = Abrir();
        int avisos = 0;
        c.Salvou += () => avisos++;
        c.Chat.EsconderGifs = true;

        c.Salvar();

        Assert.False(c.TemMudancas);
        Assert.Equal(1, avisos);
        Assert.True(Gravadas().EsconderGifs);
    }

    [Fact]
    public void NadaChegaAsOpcoesAntesDeSalvar()
    {
        var c = Abrir();

        MudarTudoNaTela(c);
        c.Chat.TipoDeChat = 1;

        Assert.Equal(ArquivoDeConfiguracoes.ParaJson(Gravadas()), ArquivoDeConfiguracoes.ParaJson(_arquivo.Opcoes));
        Assert.Equal(0, _arquivo.Opcoes.TipoDeChat);
        Assert.True(_arquivo.Opcoes.EsconderBots);
        Assert.Equal(SonsDisponiveis.PastaPadrao, _arquivo.Opcoes.PastaDosSons);
    }

    // ----- O que cada tipo grava -----

    [Fact]
    public void TipoPadrao_GravaAsOpcoesDoPadraoEApagaOEndereco()
    {
        var c = Abrir(o => { o.TipoDeChat = 2; o.EnderecoPersonalizado = "https://salvo.invalid"; });
        MudarTudoNaTela(c);

        c.Chat.TipoDeChat = 0;
        c.Salvar();

        Opcoes o = Gravadas();
        Assert.Equal(0, o.TipoDeChat);
        Assert.Equal("", o.EnderecoPersonalizado);
        Assert.True(o.ApagarMensagensAntigas);
        Assert.Equal("45", o.SegundosParaApagar);
        Assert.False(o.EsconderBots);
        Assert.True(o.EsconderGifs);
        Assert.True(o.EsconderOutrosCanais);
        Assert.Equal(Opcoes.TemaNenhum, o.Tema);
        Assert.Equal("#chat_box { color: red; }", o.CssDoTemaNenhum);
        Assert.Equal("#A8DCFF", o.CorDoTexto);
        Assert.Equal("soft", o.ContornoDasLetras);
        Assert.Equal("Arial", o.Fonte);
        Assert.True(o.MostrarHorario);
        Assert.Equal("gama.mp3", o.SomDeMensagem);
        // Os dos outros tipos ficam como estavam
        Assert.True(o.BetterTtv);
        Assert.True(o.FrankerFaceZ);
        Assert.True(o.AparenciaPadraoNoChatOficial);
        Assert.Equal("", o.CssDoChatOficial);
        Assert.Equal("", o.CssDoEnderecoPersonalizado);
    }

    [Fact]
    public void TipoPadrao_ComOTemaPadraoNaoGravaOCssDoTemaNenhum()
    {
        var c = Abrir(o => o.CssDoTemaNenhum = "salvo");
        c.Aparencia.CssDoTemaNenhum = "mexido";

        c.Salvar();

        Assert.Equal(Opcoes.TemaPadrao, Gravadas().Tema);
        Assert.Equal("salvo", Gravadas().CssDoTemaNenhum);
    }

    [Fact]
    public void TipoOficial_GravaExtensoesTextoEAparencia()
    {
        var c = Abrir();
        MudarTudoNaTela(c);

        c.Chat.TipoDeChat = 1;
        c.Salvar();

        Opcoes o = Gravadas();
        Assert.Equal(1, o.TipoDeChat);
        Assert.False(o.BetterTtv);
        Assert.False(o.MenuDeEmotesDoBetterTtv);
        Assert.False(o.Emotes7tv);
        Assert.False(o.FrankerFaceZ);
        Assert.Equal("#A8DCFF", o.CorDoTexto);
        Assert.Equal("soft", o.ContornoDasLetras);
        Assert.Equal("Arial", o.Fonte);
        Assert.True(o.MostrarHorario);
        Assert.False(o.AparenciaPadraoNoChatOficial);
        Assert.Equal(".chat-line__message { color: blue; }", o.CssDoChatOficial);
        // Os do Padrão e do endereço ficam como estavam (o som de mensagem também)
        Assert.False(o.ApagarMensagensAntigas);
        Assert.True(o.EsconderBots);
        Assert.Equal(Opcoes.TemaPadrao, o.Tema);
        Assert.Equal("", o.CssDoTemaNenhum);
        Assert.Equal("", o.EnderecoPersonalizado);
        Assert.Equal(Opcoes.SomPadrao, o.SomDeMensagem);
    }

    [Fact]
    public void TipoOficial_ComAAparenciaPadraoNaoGravaOCss()
    {
        var c = Abrir(o => o.CssDoChatOficial = "salvo");
        c.Chat.TipoDeChat = 1;
        c.Aparencia.AparenciaPadraoNoChatOficial = false;
        c.Aparencia.TextoDoEditorDoChatOficial = "mexido";
        c.Aparencia.AparenciaPadraoNoChatOficial = true;

        c.Salvar();

        Assert.True(Gravadas().AparenciaPadraoNoChatOficial);
        Assert.Equal("salvo", Gravadas().CssDoChatOficial);
    }

    [Fact]
    public void TipoEndereco_GravaSoOEnderecoEOCss()
    {
        var c = Abrir();
        MudarTudoNaTela(c);

        c.Chat.TipoDeChat = 2;
        c.Salvar();

        Opcoes o = Gravadas();
        Assert.Equal(2, o.TipoDeChat);
        Assert.Equal("https://exemplo.invalid/chat", o.EnderecoPersonalizado);
        Assert.Equal("body { color: green; }", o.CssDoEnderecoPersonalizado);
        Assert.Equal("", o.CorDoTexto);
        Assert.Equal("none", o.ContornoDasLetras);
        Assert.False(o.MostrarHorario);
        Assert.True(o.EsconderBots);
        Assert.True(o.BetterTtv);
        Assert.Equal(Opcoes.SomPadrao, o.SomDeMensagem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CSS")]
    public void TipoEndereco_CssVazioOuCssFicaVazio(string css)
    {
        var c = Abrir(o => o.TipoDeChat = 2);
        c.Aparencia.CssDoEnderecoPersonalizado = css;

        c.Salvar();

        Assert.Equal("", Gravadas().CssDoEnderecoPersonalizado);
    }

    [Fact]
    public void QualquerTipo_GravaSomGeralETwitch()
    {
        var c = Abrir();
        MudarTudoNaTela(c);
        c.Chat.TipoDeChat = 2;
        c.Geral.EsconderBordasAoAbrir = true;
        c.Geral.EsconderIconeDaBarraDeTarefas = true;
        c.Geral.ProcurarAtualizacoes = false;
        c.Geral.PermitirVariasCopias = true;
        c.Geral.AtalhoEscrever = null;
        c.Geral.AtalhoBordas = new Atalho(Key.B, ModifierKeys.Control | ModifierKeys.Shift);
        c.Twitch.CaixaDeDigitar = 1;
        c.Twitch.FecharCaixaDepoisDeEnviar = true;
        c.Twitch.MostrarResgates = true;

        c.Salvar();

        Opcoes o = Gravadas();
        Assert.Equal(30, o.SegundosEntreSons);
        Assert.Equal(0.4f, o.Volume, 3);
        Assert.Equal(_pastaDosSons, o.PastaDosSons);
        Assert.Equal(TocadorDeAviso.Padrao, o.SaidaDeSom);
        Assert.Equal(TocadorDeAviso.NomeDaPadraoGravado, o.NomeDaSaidaDeSom);
        Assert.True(o.EsconderBordasAoAbrir);
        Assert.True(o.EsconderIconeDaBarraDeTarefas);
        Assert.False(o.ProcurarAtualizacoes);
        Assert.True(o.PermitirVariasCopias);
        Assert.Null(o.AtalhoEscrever);
        Assert.Equal(new Atalho(Key.B, ModifierKeys.Control | ModifierKeys.Shift), o.AtalhoBordas);
        Assert.True(o.CaixaDaTwitch);
        Assert.True(o.FecharCaixaDepoisDeEnviar);
        Assert.True(o.MostrarResgates);
    }

    // ----- O tipo na lista muda as abas antes de salvar -----

    [Fact]
    public void TrocarOTipo_MudaAsAbasNaHoraSemGravar()
    {
        var c = Abrir();
        Assert.True(c.Chat.ComPadrao);
        Assert.True(c.Som.ComPadrao);

        c.Chat.TipoDeChat = 1;

        Assert.True(c.Chat.ComChatOficial);
        Assert.True(c.Aparencia.ComChatOficial);
        Assert.True(c.Aparencia.ComTextoDasMensagens);
        Assert.False(c.Som.ComPadrao);
        Assert.Equal(0, Gravadas().TipoDeChat);

        c.Chat.TipoDeChat = 2;
        Assert.False(c.Aparencia.ComTextoDasMensagens);
        Assert.True(c.Aparencia.ComEndereco);
    }

    [Fact]
    public void Endereco_SoApareceQuandoEleEOTipoSalvo()
    {
        Assert.Equal("", Abrir(o => { o.TipoDeChat = 0; o.EnderecoPersonalizado = "https://a.invalid"; }).Chat.EnderecoPersonalizado);
        Assert.Equal("https://a.invalid", Abrir(o => { o.TipoDeChat = 2; o.EnderecoPersonalizado = "https://a.invalid"; }).Chat.EnderecoPersonalizado);
    }

    [Fact]
    public void TipoSalvoQueNaoExiste_AparaceComoPadrao()
    {
        var c = Abrir(o => o.TipoDeChat = 7);

        Assert.Equal(0, c.Chat.TipoDeChat);
        Assert.True(c.Som.ComPadrao);
    }

    // ----- Restaurar tudo para o padrão -----

    [Fact]
    public void Restaurar_VoltaTudoMenosCanalContaListasEJanela()
    {
        var c = Abrir(o =>
        {
            o.Canal = "meucanal";
            o.Conta.Login = "hyfronx"; o.Conta.Id = "121292674"; o.Conta.Token = "tok";
            o.ListaDeUsuarios = ["amigo"];
            o.UsuariosBloqueados = ["chato"];
            o.Janela = new PosicaoDaJanela(10, 20, 300, 400, System.Windows.WindowState.Normal);
            o.TipoDeChat = 1;
            o.EsconderBots = false;
            o.Volume = 0.2f;
            o.TamanhoDoTexto = 1.5;
            o.Fundo = 40;
            o.SempreNoTopo = false;
            o.AtalhoBordas = null;
            o.CorDoDestaque = Colors.Red;
        });
        int avisos = 0;
        c.Salvou += () => avisos++;
        c.Chat.EsconderGifs = true; // mudança na tela que se perde

        c.Restaurar();

        Opcoes o = Gravadas();
        Assert.Equal("meucanal", o.Canal);
        Assert.Equal("hyfronx", o.Conta.Login);
        Assert.Equal(["amigo"], o.ListaDeUsuarios);
        Assert.Equal(["chato"], o.UsuariosBloqueados);
        Assert.Equal(10, o.Janela!.Esquerda);
        var padrao = new Opcoes();
        Assert.Equal(padrao.TipoDeChat, o.TipoDeChat);
        Assert.True(o.EsconderBots);
        Assert.Equal(1f, o.Volume);
        Assert.Equal(Opcoes.TamanhoDoTextoPadrao, o.TamanhoDoTexto);
        Assert.Equal(Opcoes.FundoPadrao, o.Fundo);
        Assert.True(o.SempreNoTopo);
        Assert.Equal(padrao.AtalhoBordas, o.AtalhoBordas);
        Assert.Equal(padrao.CorDoDestaque, o.CorDoDestaque);
        Assert.True(File.Exists(Path.Combine(_pasta, ArquivoDeConfiguracoes.PastaAntesDeRestaurar, ArquivoDeConfiguracoes.NomeDoArquivo)));

        // As abas mostram os valores restaurados, sem mudanças pendentes, e o chat aplica
        Assert.Equal(0, c.Chat.TipoDeChat);
        Assert.True(c.Som.ComPadrao);
        Assert.False(c.Chat.EsconderGifs);
        Assert.True(c.Chat.EsconderBots);
        Assert.Equal(100, c.Som.Volume);
        Assert.False(c.TemMudancas);
        Assert.Equal(1, avisos);
    }

    // ----- "Não procurar atualizações automaticamente" com a janela aberta (decisão 2) -----

    [Fact]
    public void ProcuraAutomaticaDesligada_InterruptorAcompanhaSemContarComoMudanca()
    {
        var c = Abrir(o => o.ProcurarAtualizacoes = true);

        c.ProcuraAutomaticaDesligada();

        Assert.False(c.Geral.ProcurarAtualizacoes);
        Assert.False(c.TemMudancas);
    }

    [Fact]
    public void ProcuraAutomaticaDesligada_OutrasMudancasContinuamSemSalvar()
    {
        var c = Abrir(o => o.ProcurarAtualizacoes = true);
        c.Chat.EsconderGifs = true;

        c.ProcuraAutomaticaDesligada();
        Assert.True(c.TemMudancas);

        c.Chat.EsconderGifs = false;
        Assert.False(c.TemMudancas);
    }

    // ----- Dicas que dependem do atalho de "Escrever no chat" -----

    [Fact]
    public void Dicas_ComAtalhoCitamOAtalho()
    {
        var atalho = new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt);

        Assert.Equal("A caixa \"Escrever no chat…\" embaixo do chat: abra com o balão de conversa, na barra laranja. Envia com a " +
                     "conta conectada acima. No jogo, aperte Ctrl + Alt + F11 para abrir ou fechar a caixa por cima do jogo.",
            LogicaTwitch.TextoDaDicaDaCaixa(false, atalho));
        Assert.EndsWith("\"Faça login\". No jogo, aperte Ctrl + Alt + F11 para abrir ou fechar a caixa por cima do jogo.",
            LogicaTwitch.TextoDaDicaDaCaixa(true, atalho));
        Assert.StartsWith("A caixa da Twitch, com a lista de emotes, respostas e comandos (só no Chat oficial da Twitch;",
            LogicaTwitch.TextoDaDicaDaCaixa(true, atalho));
        Assert.Equal("Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, " +
                     "ela continua aberta para a próxima mensagem; feche com Ctrl + Alt + F11 de novo, Esc ou o × da caixa.",
            LogicaTwitch.TextoDaDicaDeFechar(atalho));
    }

    [Fact]
    public void Dicas_SemAtalhoNaoFalamDoAtalho()
    {
        Assert.Equal("A caixa \"Escrever no chat…\" embaixo do chat: abra com o balão de conversa, na barra laranja. Envia com a " +
                     "conta conectada acima.", LogicaTwitch.TextoDaDicaDaCaixa(false, null));
        Assert.EndsWith("depois em \"Faça login\".", LogicaTwitch.TextoDaDicaDaCaixa(true, null));
        Assert.Equal("Com a caixa aberta pelo atalho no jogo: ligado, ela fecha ao enviar e o jogo volta para a frente. Desligado, " +
                     "ela continua aberta para a próxima mensagem.", LogicaTwitch.TextoDaDicaDeFechar(null));
        Assert.DoesNotContain("aperte", LogicaTwitch.TextoDaDicaDaCaixa(false, new Atalho(Key.None, ModifierKeys.None)));
    }

    [Fact]
    public void Dicas_CitamOAtalhoSalvoNaoOQueEstaSendoEditado()
    {
        var c = Abrir(o => o.AtalhoEscrever = new Atalho(Key.F11, ModifierKeys.Control | ModifierKeys.Alt));

        c.Geral.AtalhoEscrever = new Atalho(Key.F10, ModifierKeys.Control);
        Assert.Contains("Ctrl + Alt + F11", c.Twitch.DicaDaCaixa);
        Assert.Contains("Ctrl + Alt + F11", c.Twitch.DicaDeFecharDepoisDeEnviar);

        c.Salvar();
        Assert.Contains("Ctrl + F10", c.Twitch.DicaDaCaixa);

        c.Geral.AtalhoEscrever = null;
        c.Salvar();
        Assert.DoesNotContain("aperte", c.Twitch.DicaDaCaixa);
    }

    [Fact]
    public void DicaDaCaixa_MudaComOItemEscolhido()
    {
        var c = Abrir();
        string doApp = c.Twitch.DicaDaCaixa;

        c.Twitch.CaixaDeDigitar = 1;

        Assert.NotEqual(doApp, c.Twitch.DicaDaCaixa);
        Assert.StartsWith("A caixa da Twitch", c.Twitch.DicaDaCaixa);
    }

    // ----- Som -----

    [Fact]
    public void Som_QueSumiuDaPastaApareceComoNaoEncontradoESalvarMantem()
    {
        var c = Abrir(o => { o.PastaDosSons = _pastaDosSons; o.SomDeMensagem = "sumiu-daqui.wav"; });

        Assert.Equal("sumiu-daqui.wav", c.Som.SomEscolhido);
        Assert.Contains(c.Som.Sons, s => s.Nome == "Sumiu daqui (não encontrado)" && s.Valor == "sumiu-daqui.wav");
        Assert.False(c.TemMudancas);

        c.Salvar();
        Assert.Equal("sumiu-daqui.wav", Gravadas().SomDeMensagem);

        // "Ouvir" não toca nada (o arquivo não existe)
        c.Som.Ouvir();
        Assert.Null(_previas.Single().Arquivo);
    }

    [Fact]
    public void Som_ListaNenhumESonsDaPasta()
    {
        var c = Abrir(o => { o.PastaDosSons = _pastaDosSons; o.SomDeMensagem = "beta-som.wav"; });

        Assert.Equal(["Nenhum", "Alfa", "Beta som", "Gama"], c.Som.Sons.Select(s => s.Nome));
        Assert.Equal("beta-som.wav", c.Som.SomEscolhido);
    }

    [Fact]
    public void Som_TrocarDePastaMantemOSomSeExisteNelaSenaoFicaNenhum()
    {
        string outra = Path.Combine(_pasta, "outra");
        Directory.CreateDirectory(outra);
        File.WriteAllBytes(Path.Combine(outra, "alfa.wav"), []);
        var c = Abrir(o => { o.PastaDosSons = _pastaDosSons; o.SomDeMensagem = "alfa.wav"; });

        c.Som.MudarPasta(outra);
        Assert.Equal("alfa.wav", c.Som.SomEscolhido);

        c.Som.SomEscolhido = "alfa.wav";
        c.Som.MudarPasta(_pastaDosSons);
        c.Som.SomEscolhido = "gama.mp3";
        c.Som.MudarPasta(outra);
        Assert.Equal(SonsDisponiveis.Nenhum, c.Som.SomEscolhido);
        Assert.Equal(outra, c.Som.TextoDaPasta);

        c.Som.UsarPastaPadraoCommand.Execute(null);
        Assert.Equal(LogicaSom.TextoDaPastaPadrao, c.Som.TextoDaPasta);
    }

    [Fact]
    public void Som_QueSumiuNaoVoltaDepoisDeTrocarDePasta()
    {
        var c = Abrir(o => { o.PastaDosSons = _pastaDosSons; o.SomDeMensagem = "sumiu.wav"; });

        c.Som.MudarPasta(_pastaDosSons);

        Assert.Equal(SonsDisponiveis.Nenhum, c.Som.SomEscolhido);
        Assert.DoesNotContain(c.Som.Sons, s => s.Nome.EndsWith("(não encontrado)"));
    }

    [Fact]
    public void Som_OuvirUsaOVolumeEASaidaDaTela()
    {
        var c = Abrir(o => { o.PastaDosSons = _pastaDosSons; o.SomDeMensagem = "alfa.wav"; });
        c.Som.Volume = 33.4;

        c.Som.Ouvir();

        var previa = _previas.Single();
        Assert.Equal(Path.Combine(_pastaDosSons, "alfa.wav"), previa.Arquivo);
        Assert.Equal(0.33f, previa.Volume, 3);
        Assert.Equal(TocadorDeAviso.Padrao, previa.Saida);
    }

    [Fact]
    public void QuandoTocar_ValorForaDaListaEntraComoItem()
    {
        var c = Abrir(o => o.SegundosEntreSons = 45);

        Assert.Equal("45", c.Som.QuandoTocar);
        Assert.Equal("No máximo 1 vez a cada 45 s", c.Som.OpcoesDeQuandoTocar.Last().Nome);
        Assert.Equal(7, c.Som.OpcoesDeQuandoTocar.Count);
    }

    [Fact]
    public void SaidaDeSomQueNaoExisteMais_AparaceComoPadraoDoWindows()
    {
        var c = Abrir(o => { o.SaidaDeSom = 42; o.NomeDaSaidaDeSom = "Fone que sumiu"; });

        Assert.Equal(TocadorDeAviso.Padrao, c.Som.SaidaEscolhida);
    }

    // ----- Aparência -----

    [Fact]
    public void ChatOficial_SemCssGuardadoOEditorComecaComOPadrao()
    {
        var c = Abrir();

        c.Aparencia.AparenciaPadraoNoChatOficial = false;

        Assert.Equal(CssDoChat.PadraoDoChatOficial, c.Aparencia.TextoDoEditorDoChatOficial);
    }

    [Fact]
    public void ChatOficial_ComAAparenciaPadraoOEditorMostraOPadraoENaoMuda()
    {
        var c = Abrir(o => { o.AparenciaPadraoNoChatOficial = true; o.CssDoChatOficial = "meu css"; });

        c.Aparencia.TextoDoEditorDoChatOficial = "tentativa";
        Assert.Equal(CssDoChat.PadraoDoChatOficial, c.Aparencia.TextoDoEditorDoChatOficial);

        // Desligada, mostra o CSS guardado; ligar e desligar de novo não perde o que foi digitado
        c.Aparencia.AparenciaPadraoNoChatOficial = false;
        Assert.Equal("meu css", c.Aparencia.TextoDoEditorDoChatOficial);
        c.Aparencia.TextoDoEditorDoChatOficial = "meu css novo";
        c.Aparencia.AparenciaPadraoNoChatOficial = true;
        c.Aparencia.AparenciaPadraoNoChatOficial = false;
        Assert.Equal("meu css novo", c.Aparencia.TextoDoEditorDoChatOficial);
    }

    [Fact]
    public void CssDoTemaNenhum_VazioMostraOExemploDoTema()
    {
        Assert.Equal(CssDoChat.ExemploDoTemaNenhum, Abrir().Aparencia.CssDoTemaNenhum);
        Assert.Equal("salvo", Abrir(o => o.CssDoTemaNenhum = "salvo").Aparencia.CssDoTemaNenhum);
    }

    [Fact]
    public void CssDoEndereco_VazioMostraOExemploSoSeOEnderecoNaoEOTipoSalvo()
    {
        Assert.Equal(LogicaAparencia.ExemploDoCssDoEndereco, Abrir(o => o.TipoDeChat = 0).Aparencia.CssDoEnderecoPersonalizado);
        Assert.Equal("", Abrir(o => o.TipoDeChat = 2).Aparencia.CssDoEnderecoPersonalizado);
        Assert.Equal("salvo", Abrir(o => { o.TipoDeChat = 0; o.CssDoEnderecoPersonalizado = "salvo"; }).Aparencia.CssDoEnderecoPersonalizado);
    }

    [Theory]
    [InlineData("#ABCDEF", "", "theme", "Comic Sans", "theme")]
    [InlineData("#FFF3A6", "none", "none", "Verdana", "Verdana")]
    public void TextoDasMensagens_ValorForaDaListaApareceComoOPrimeiro(string cor, string contorno, string contornoNaTela, string fonte,
        string fonteNaTela)
    {
        var c = Abrir(o => { o.CorDoTexto = cor; o.ContornoDasLetras = contorno; o.Fonte = fonte; });

        Assert.Equal(cor == "#ABCDEF" ? "" : cor, c.Aparencia.CorDoTexto);
        Assert.Equal(contornoNaTela, c.Aparencia.ContornoDasLetras);
        Assert.Equal(fonteNaTela, c.Aparencia.Fonte);
    }

    // ----- Filtros do chat -----

    [Fact]
    public void FiltrosSalvos_AvisamOChatSemMexerNasMudancasDaTela()
    {
        var c = Abrir();
        int avisos = 0;
        c.Salvou += () => avisos++;
        c.Chat.EsconderGifs = true;

        var filtros = c.Chat.NovaLogicaDosFiltros();
        filtros.NovoBloqueado = "chato";
        filtros.Salvar();

        Assert.Equal(1, avisos);
        Assert.Equal(["chato"], Gravadas().UsuariosBloqueados);
        Assert.False(Gravadas().EsconderGifs);
        Assert.True(c.TemMudancas);
    }

    // ----- Decisão 6 -----

    [Fact]
    public void PermitirCliqueComBordas_SaiuDoArquivoEOArquivoAntigoComElaAindaAbre()
    {
        Assert.DoesNotContain("PermitirClique", ArquivoDeConfiguracoes.ParaJson(new Opcoes()));

        Opcoes lidas = ArquivoDeConfiguracoes.DeJson("""{ "Versao": 1, "Canal": "abc", "PermitirCliqueComBordas": false }""");
        Assert.Equal("abc", lidas.Canal);
    }
}
