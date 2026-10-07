using System.Windows.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.Chat;
using OlhoNoChat.Twitch;
using Acao = OlhoNoChat.Janelas.Chat.LogicaCaixaDeEscrever.AcaoDoBotao;
using AcaoDoAtalho = OlhoNoChat.Janelas.Chat.LogicaCaixaDeEscrever.AcaoDoAtalho;

namespace OlhoNoChat.Testes.Janelas;

public class LogicaCaixaDeEscreverTestes
{
    private readonly Opcoes _opcoes = new() { Canal = "hyfronx" };
    private bool _conectada = true;
    private bool _podeEnviar = true;
    private EnvioDeMensagem.Resultado _resultado = new(EnvioDeMensagem.Situacao.Enviada);
    private readonly List<(string Canal, string Texto)> _enviadas = [];

    private LogicaCaixaDeEscrever Nova() => new(() => _opcoes, () => _conectada, () => _podeEnviar, () => "Hyfronx",
        (canal, texto) =>
        {
            _enviadas.Add((canal, texto));
            return Task.FromResult(_resultado);
        });

    [Fact]
    public void CaixaDoApp_PrecisaDeContaQuePodeEnviarECanal()
    {
        Assert.True(Nova().CaixaDoAppDisponivel);

        _podeEnviar = false;
        Assert.False(Nova().CaixaDoAppDisponivel);
        _podeEnviar = true;
        _conectada = false;
        Assert.False(Nova().CaixaDoAppDisponivel);
        _conectada = true;
        _opcoes.Canal = string.Empty;
        Assert.False(Nova().CaixaDoAppDisponivel);
        _opcoes.Canal = "hyfronx";
        _opcoes.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
        Assert.False(Nova().CaixaDoAppDisponivel);
        Assert.Equal(string.Empty, Nova().Canal);
    }

    [Fact]
    public void CaixaDaTwitch_SoNoChatOficialComAPaginaDeleAberta()
    {
        _opcoes.CaixaDaTwitch = true;
        Assert.False(Nova().UsaCaixaDaTwitch); // no Padrão a opção não vale
        Assert.True(Nova().CaixaDoAppDisponivel);

        _opcoes.TipoDeChat = (int)TipoDeChat.ChatOficial;
        var logica = Nova();
        Assert.True(logica.UsaCaixaDaTwitch);
        Assert.False(logica.CaixaDoAppDisponivel);
        Assert.True(logica.CaixaDaTwitchDisponivel(paginaDoChatOficialAberta: true));
        Assert.False(logica.CaixaDaTwitchDisponivel(paginaDoChatOficialAberta: false));
    }

    [Fact]
    public void Botao_AbreACaixaDoApp_EClicarDeNovoFecha()
    {
        var logica = Nova();
        Assert.Equal(Acao.AbrirCaixaDoApp, logica.AoClicarNoBotao(false));

        logica.AbrirPeloBotao();
        Assert.True(logica.Aberta(bordasVisiveis: true));
        Assert.True(logica.CaixaDoAppNaTela(bordasVisiveis: true));
        Assert.Equal(Acao.Fechar, logica.AoClicarNoBotao(false));

        logica.FecharOBotao();
        Assert.False(logica.Aberta(bordasVisiveis: true));
    }

    [Fact]
    public void Botao_SemConta_AvisaQueNaoDa()
    {
        _conectada = false;
        Assert.Equal(Acao.AvisarQueNaoDa, Nova().AoClicarNoBotao(false));
    }

    [Fact]
    public void Botao_ComACaixaDaTwitchEscolhida_AbreADaTwitch()
    {
        _opcoes.TipoDeChat = (int)TipoDeChat.ChatOficial;
        _opcoes.CaixaDaTwitch = true;
        Assert.Equal(Acao.AbrirCaixaDaTwitch, Nova().AoClicarNoBotao(paginaDoChatOficialAberta: true));
        Assert.Equal(Acao.AvisarQueNaoDa, Nova().AoClicarNoBotao(paginaDoChatOficialAberta: false));
    }

    [Fact]
    public void AbertaPeloBotao_SomeComAsBordasOcultasENaoVolta()
    {
        var logica = Nova();
        logica.AbrirPeloBotao();

        Assert.False(logica.Aberta(bordasVisiveis: false));
        logica.BordasOcultas();
        Assert.False(logica.Aberta(bordasVisiveis: true));
    }

    [Fact]
    public void Atalho_AbreEApertadoDeNovoFecha_DevolvendoOFoco()
    {
        var logica = Nova();
        Assert.Equal(AcaoDoAtalho.Abrir, logica.AoApertarOAtalho(bordasVisiveis: false, janelaAtiva: false, false));

        logica.Comecar(naCaixaDaTwitch: false, daTwitchPeloBotao: false, devolverFocoPara: new IntPtr(123));
        Assert.True(logica.Aberta(bordasVisiveis: false)); // por cima do jogo, também sem as bordas
        Assert.True(logica.CaixaDoAppNaTela(bordasVisiveis: false));
        Assert.Equal(AcaoDoAtalho.Fechar, logica.AoApertarOAtalho(false, true, false));

        Assert.Equal(new IntPtr(123), logica.Terminar());
        Assert.False(logica.PeloAtalho);
        Assert.Equal(IntPtr.Zero, logica.DevolverFocoPara);
    }

    [Fact]
    public void Atalho_ComACaixaDoBotaoAberta_FechaSoComAJanelaAtiva()
    {
        var logica = Nova();
        logica.AbrirPeloBotao();

        Assert.Equal(AcaoDoAtalho.Fechar, logica.AoApertarOAtalho(bordasVisiveis: true, janelaAtiva: true, false));
        // O jogo está na frente: o atalho só dá o foco à caixa
        Assert.Equal(AcaoDoAtalho.Abrir, logica.AoApertarOAtalho(bordasVisiveis: true, janelaAtiva: false, false));
    }

    [Fact]
    public void Atalho_SemNenhumaCaixa_AvisaQueNaoDa()
    {
        _conectada = false;
        Assert.Equal(AcaoDoAtalho.AvisarQueNaoDa, Nova().AoApertarOAtalho(false, false, false));
    }

    [Fact]
    public void FechaDepoisDeEnviar_SoAbertaPeloAtalhoComAOpcao()
    {
        var logica = Nova();
        _opcoes.FecharCaixaDepoisDeEnviar = true;
        logica.AbrirPeloBotao();
        Assert.False(logica.FechaDepoisDeEnviar);

        logica.Comecar(false, false, IntPtr.Zero);
        Assert.True(logica.FechaDepoisDeEnviar);
        _opcoes.FecharCaixaDepoisDeEnviar = false;
        Assert.False(logica.FechaDepoisDeEnviar);
    }

    [Fact]
    public void ContaDesconectada_ACaixaDoBotaoFecha()
    {
        var logica = Nova();
        logica.AbrirPeloBotao();
        _conectada = false;
        logica.Atualizar();

        Assert.False(logica.AbertaPeloBotao);
    }

    [Theory]
    [InlineData(TipoDeChat.EnderecoPersonalizado, "hyfronx", true, true,
        "Para escrever no chat, escolha o tipo de chat \"Padrão\" ou \"Chat oficial da Twitch\" em Configurações > Chat.")]
    [InlineData(TipoDeChat.Padrao, "", true, true, "Para escrever no chat, escolha o canal na faixa de cima do chat.")]
    [InlineData(TipoDeChat.Padrao, "hyfronx", false, true,
        "Para escrever no chat, conecte sua conta da Twitch em Configurações > Twitch.")]
    [InlineData(TipoDeChat.Padrao, "hyfronx", true, false,
        "Para escrever no chat, conecte sua conta de novo em Configurações > Twitch: a Twitch precisa dar a permissão de escrever.")]
    public void TextoQueNaoDa_NaOrdem(TipoDeChat tipo, string canal, bool conectada, bool podeEnviar, string esperado)
    {
        _opcoes.TipoDeChat = (int)tipo;
        _opcoes.Canal = canal;
        _conectada = conectada;
        _podeEnviar = podeEnviar;

        Assert.Equal(esperado, Nova().TextoQueNaoDa);
    }

    [Fact]
    public void TextoQueDa_ComContaBordasEAtalho()
    {
        var logica = Nova();
        Assert.Equal("Neste chat você pode escrever. Clique no balão de conversa, na barra laranja, para abrir a caixa. " +
                     "No jogo, aperte Ctrl + Alt + F11 para abrir ou fechar a caixa de escrever.", logica.TextoQueDa(bordasVisiveis: true));
        Assert.Equal("Neste chat você pode escrever. No jogo, aperte Ctrl + Alt + F11 para abrir ou fechar a caixa de escrever.",
            logica.TextoQueDa(bordasVisiveis: false));

        _conectada = false;
        _opcoes.AtalhoEscrever = null;
        Assert.Equal("Neste chat você pode escrever depois de conectar sua conta em Configurações > Twitch. " +
                     "Para abrir a caixa no jogo, escolha um atalho em Configurações > Geral.", Nova().TextoQueDa(true));

        _opcoes.Canal = string.Empty;
        Assert.Null(Nova().TextoQueDa(true));
    }

    [Fact]
    public void Dicas()
    {
        var logica = Nova();
        Assert.Equal("Vai para o chat de hyfronx como Hyfronx", logica.DicaDaCaixa);
        Assert.Equal("Fechar a caixa (Esc).\nAtalho: Ctrl + Alt + F11", logica.DicaDoFechar);
        Assert.Equal("Escrever no chat da Twitch\nAtalho: Ctrl + Alt + F11", logica.DicaDoBotaoEscrever(true));

        logica.Comecar(false, false, IntPtr.Zero);
        Assert.Equal("Fechar a caixa e voltar para o jogo (Esc).\nAtalho: Ctrl + Alt + F11", logica.DicaDoFechar);
        Assert.Equal("Fechar a caixa de escrever\nAtalho: Ctrl + Alt + F11", logica.DicaDoBotaoEscrever(true));

        _opcoes.AtalhoEscrever = new Atalho(Key.None, ModifierKeys.None);
        Assert.Equal("Fechar a caixa e voltar para o jogo (Esc).", logica.DicaDoFechar);
    }

    [Fact]
    public async Task Enviar_SemEspacosNasPontas_ECaixaVaziaDepois()
    {
        var logica = Nova();
        logica.Texto = "  olá chat  ";

        Assert.True(await logica.EnviarAsync());
        Assert.Equal([("hyfronx", "olá chat")], _enviadas);
        Assert.Equal(string.Empty, logica.Texto);
    }

    [Fact]
    public async Task Enviar_Vazio_NaoFazNada()
    {
        var logica = Nova();
        logica.Texto = "   ";

        Assert.False(await logica.EnviarAsync());
        Assert.Empty(_enviadas);
    }

    [Fact]
    public async Task Enviar_Falhou_OTextoFicaEOErroAparece_DigitarApaga()
    {
        _resultado = new EnvioDeMensagem.Resultado(EnvioDeMensagem.Situacao.Falhou, "Não foi possível enviar. Confira sua internet.");
        var logica = Nova();
        logica.Texto = "oi";

        Assert.False(await logica.EnviarAsync());
        Assert.Equal("oi", logica.Texto);
        Assert.Equal("Não foi possível enviar. Confira sua internet.", logica.Status);

        logica.Texto = "oi!";
        Assert.Equal(string.Empty, logica.Status);
    }

    [Fact]
    public async Task Enviar_EnquantoEnvia_OutroPedidoEIgnorado()
    {
        var resposta = new TaskCompletionSource<EnvioDeMensagem.Resultado>();
        int envios = 0;
        var logica = new LogicaCaixaDeEscrever(() => _opcoes, () => true, () => true, () => "x", (_, _) =>
        {
            envios++;
            return resposta.Task;
        });
        logica.Texto = "oi";

        Task<bool> primeiro = logica.EnviarAsync();
        Assert.False(logica.PodeEnviar);
        Assert.False(await logica.EnviarAsync());

        resposta.SetResult(new EnvioDeMensagem.Resultado(EnvioDeMensagem.Situacao.Enviada));
        Assert.True(await primeiro);
        Assert.Equal(1, envios);
        Assert.True(logica.PodeEnviar);
    }

    [Theory]
    [InlineData("", 0, 0, "Kappa ", 6)]
    [InlineData("oi", 2, 0, "oi Kappa ", 9)]
    [InlineData("oi ", 3, 0, "oi Kappa ", 9)]
    [InlineData("oi tudo", 2, 0, "oi Kappa tudo", 8)]
    [InlineData("oi tudo", 3, 4, "oi Kappa ", 9)]      // troca a seleção
    [InlineData("oitudo", 2, 0, "oi Kappa tudo", 9)]
    public void ComEmote_EspacosEmVolta(string texto, int inicio, int selecao, string esperado, int cursor)
    {
        Assert.Equal(esperado, LogicaCaixaDeEscrever.ComEmote(texto, inicio, selecao, "Kappa", out int depois));
        Assert.Equal(cursor, depois);
    }

    [Fact]
    public void ComEmote_PassandoDe500_NaoInsere()
    {
        string quase = new('a', 495);
        Assert.Null(LogicaCaixaDeEscrever.ComEmote(quase, 495, 0, "Kappa", out _));
        Assert.NotNull(LogicaCaixaDeEscrever.ComEmote(new string('a', 493), 493, 0, "Kappa", out _));
    }
}
