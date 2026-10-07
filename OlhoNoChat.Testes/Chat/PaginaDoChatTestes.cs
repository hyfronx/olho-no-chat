using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Chat;

/// <summary>
/// Qual página cada tipo de chat abre, o que ela só lê ao abrir (mudou → recarrega) e o que vale ao vivo ao salvar.
/// </summary>
public sealed class PaginaDoChatTestes
{
    private static Opcoes Com(Action<Opcoes> mudar)
    {
        var opcoes = new Opcoes();
        mudar(opcoes);
        return opcoes;
    }

    private static readonly string BoasVindas = new Uri(InfoDoApp.PaginaDeBoasVindas).AbsoluteUri;

    // --- Endereço de cada tipo ---

    [Fact]
    public void Padrao_TemaPadraoPoeOParametroDoTema()
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o => o.Canal = "hyfronx"));

        Assert.IsType<ChatPadrao>(pagina);
        Assert.Equal("https://olhonochat.example/chat.html?canal=hyfronx&tema=padrao", pagina.Endereco);
        Assert.Equal("hyfronx", pagina.Canal);
    }

    [Fact]
    public void Padrao_TemaNenhumNaoPoeOParametro()
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o => { o.Canal = "hyfronx"; o.Tema = Opcoes.TemaNenhum; }));

        Assert.Equal("https://olhonochat.example/chat.html?canal=hyfronx", pagina.Endereco);
    }

    [Fact]
    public void Padrao_CanalVaiComEscapeDeEndereco()
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o => o.Canal = "a b&c"));

        Assert.StartsWith("https://olhonochat.example/chat.html?canal=a%20b%26c&", pagina.Endereco);
    }

    [Fact]
    public void CanalSalvoComoLinkViraONome()
    {
        var opcoes = Com(o => o.Canal = "https://www.twitch.tv/Hyfronx/videos");

        Assert.Equal("Hyfronx", PaginaDoChat.CanalSalvo(opcoes));
        Assert.Equal("https://olhonochat.example/chat.html?canal=Hyfronx&tema=padrao", PaginaDoChat.DasOpcoes(opcoes).Endereco);
    }

    [Fact]
    public void ChatOficial_PopoutDaTwitch()
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o => { o.Canal = "hyfronx"; o.TipoDeChat = (int)TipoDeChat.ChatOficial; }));

        Assert.IsType<ChatOficialDaTwitch>(pagina);
        Assert.Equal("https://www.twitch.tv/popout/hyfronx/chat?popout=", pagina.Endereco);
        Assert.Equal("hyfronx", pagina.Canal);
    }

    [Fact]
    public void EnderecoPersonalizado_ExatamenteComoFoiDigitado()
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o =>
        {
            o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
            o.EnderecoPersonalizado = " https://streamelements.com/overlay/x ";
            o.Canal = "hyfronx";
        }));

        Assert.IsType<ChatEnderecoPersonalizado>(pagina);
        Assert.Equal(" https://streamelements.com/overlay/x ", pagina.Endereco);
        Assert.Null(pagina.Canal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SemCanal_BoasVindasQueMandamDigitarOCanal(int tipo)
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o => { o.TipoDeChat = tipo; o.Canal = "  "; }));

        Assert.IsType<PaginaDeBoasVindas>(pagina);
        Assert.Equal(BoasVindas + "?canal", pagina.Endereco);
        Assert.Null(pagina.Canal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnderecoVazio_BoasVindasQueMandamAbrirAsConfiguracoes(string endereco)
    {
        var pagina = PaginaDoChat.DasOpcoes(Com(o =>
        {
            o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
            o.EnderecoPersonalizado = endereco;
            o.Canal = "hyfronx";
        }));

        Assert.IsType<PaginaDeBoasVindas>(pagina);
        Assert.Equal(BoasVindas, pagina.Endereco);
    }

    [Fact]
    public void TipoQueNaoExisteMais_ViraPadrao()
    {
        Assert.Equal(TipoDeChat.Padrao, TiposDeChat.Ler(3));
        Assert.IsType<ChatPadrao>(PaginaDoChat.DasOpcoes(Com(o => { o.TipoDeChat = 3; o.Canal = "hyfronx"; })));
    }

    // --- Quando recarrega ---

    private static bool Recarrega(Action<Opcoes> antes, Action<Opcoes> depois)
    {
        var opcoes = new Opcoes();
        antes(opcoes);
        string chave = PaginaDoChat.DasOpcoes(opcoes).ChaveDeRecarga;
        depois(opcoes);
        return PaginaDoChat.DasOpcoes(opcoes).ChaveDeRecarga != chave;
    }

    [Fact]
    public void Padrao_RecarregaComCanalETema()
    {
        Action<Opcoes> padrao = o => o.Canal = "hyfronx";

        Assert.True(Recarrega(padrao, o => o.Canal = "outro"));
        Assert.True(Recarrega(padrao, o => o.Tema = Opcoes.TemaNenhum));
    }

    [Fact]
    public void Padrao_OResto_AoVivo()
    {
        Action<Opcoes> padrao = o => o.Canal = "hyfronx";

        Assert.False(Recarrega(padrao, o =>
        {
            o.EsconderBots = false;
            o.EsconderGifs = true;
            o.EsconderOutrosCanais = true;
            o.ApagarMensagensAntigas = true;
            o.DestacarUsuarios = true;
            o.ListaDeUsuarios.Add("fulano");
            o.UsuariosBloqueados.Add("bot");
            o.CorDoDestaque = System.Windows.Media.Colors.Red;
            o.CorDoTexto = "#FFFFFF";
            o.ContornoDasLetras = "soft";
            o.Fonte = "Arial";
            o.MostrarHorario = true;
            o.SomDeMensagem = "None";
            o.CssDoTemaNenhum = "body {}";
            // Opções de outros tipos também não
            o.BetterTtv = false;
            o.CssDoChatOficial = "x";
            o.CssDoEnderecoPersonalizado = "y";
        }));
    }

    [Fact]
    public void ChatOficial_RecarregaComCanalExtensoesEAparencia()
    {
        Action<Opcoes> oficial = o => { o.Canal = "hyfronx"; o.TipoDeChat = (int)TipoDeChat.ChatOficial; };

        Assert.True(Recarrega(oficial, o => o.Canal = "outro"));
        Assert.True(Recarrega(oficial, o => o.BetterTtv = false));
        Assert.True(Recarrega(oficial, o => o.Emotes7tv = false));
        Assert.True(Recarrega(oficial, o => o.MenuDeEmotesDoBetterTtv = false));
        Assert.True(Recarrega(oficial, o => o.FrankerFaceZ = false));
        Assert.True(Recarrega(oficial, o => o.AparenciaPadraoNoChatOficial = false));
        Assert.True(Recarrega(oficial, o => o.CssDoChatOficial = "body { color: red; }"));
    }

    [Fact]
    public void ChatOficial_TextoHorarioECaixa_AoVivo()
    {
        Action<Opcoes> oficial = o => { o.Canal = "hyfronx"; o.TipoDeChat = (int)TipoDeChat.ChatOficial; };

        Assert.False(Recarrega(oficial, o =>
        {
            o.CorDoTexto = "#FFF3A6";
            o.ContornoDasLetras = "theme";
            o.Fonte = "Verdana";
            o.MostrarHorario = true;
            o.CaixaDaTwitch = true;
            o.Tema = Opcoes.TemaNenhum;
        }));
    }

    [Fact]
    public void EnderecoPersonalizado_RecarregaComEnderecoECss()
    {
        Action<Opcoes> endereco = o =>
        {
            o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
            o.EnderecoPersonalizado = "https://exemplo.com/chat";
        };

        Assert.True(Recarrega(endereco, o => o.EnderecoPersonalizado = "https://exemplo.com/outro"));
        Assert.True(Recarrega(endereco, o => o.CssDoEnderecoPersonalizado = "body {}"));
        Assert.False(Recarrega(endereco, o => { o.Canal = "outro"; o.CorDoTexto = "#FFFFFF"; }));
    }

    [Fact]
    public void TrocarDeTipo_Recarrega()
    {
        Action<Opcoes> padrao = o => o.Canal = "hyfronx";

        Assert.True(Recarrega(padrao, o => o.TipoDeChat = (int)TipoDeChat.ChatOficial));
        Assert.True(Recarrega(padrao, o => { o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado; o.EnderecoPersonalizado = "https://a"; }));
        Assert.True(Recarrega(padrao, o => o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado)); // boas-vindas
    }

    [Fact]
    public void BoasVindas_SoRecarregaQuandoOTextoMuda()
    {
        // Sem canal, Padrão e chat oficial mostram as mesmas boas-vindas; o endereço vazio mostra outras
        Assert.False(Recarrega(o => o.Canal = "", o => o.TipoDeChat = (int)TipoDeChat.ChatOficial));
        Assert.False(Recarrega(o => o.Canal = "", o => o.Tema = Opcoes.TemaNenhum));
        Assert.True(Recarrega(o => o.Canal = "", o => o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado));
        Assert.True(Recarrega(o => o.Canal = "", o => o.Canal = "hyfronx"));
    }

    // --- O que vai para a página ---

    [Fact]
    public void Padrao_CssDepoisIniciar_NumPedidoSo()
    {
        var opcoes = Com(o => o.Canal = "hyfronx");
        string script = PaginaDoChat.DasOpcoes(opcoes).ScriptAoCarregar(opcoes)!;

        int css = script.IndexOf("onc-custom-css", StringComparison.Ordinal);
        int iniciar = script.IndexOf("window.oncChat && window.oncChat.start(", StringComparison.Ordinal);
        Assert.True(css >= 0 && iniciar > css);
        Assert.Contains(ContratoComAPagina.JsonDeConfiguracoes(opcoes), script);
    }

    [Fact]
    public void Padrao_AoVivo_ApplyEOCss_EPrecisaDaPagina()
    {
        var opcoes = Com(o => { o.Canal = "hyfronx"; o.MostrarHorario = true; });
        var pagina = PaginaDoChat.DasOpcoes(opcoes);

        Assert.True(pagina.RecarregaSeNaoAplicar);
        string script = pagina.ScriptAoVivo(opcoes)!;
        Assert.Contains("if (!window.oncChat) return false;", script);
        Assert.Contains("window.oncChat.apply(opcoes);", script);
        Assert.Contains("return true;", script);
        Assert.Contains(".time_stamp", script);
    }

    [Fact]
    public void ChatOficial_AoVivo_SoOCss()
    {
        var opcoes = Com(o => { o.Canal = "hyfronx"; o.TipoDeChat = (int)TipoDeChat.ChatOficial; });
        var pagina = PaginaDoChat.DasOpcoes(opcoes);

        Assert.False(pagina.RecarregaSeNaoAplicar);
        Assert.Equal(ContratoComAPagina.PorOCss(CssDoChat.DoChatOficial(opcoes)), pagina.ScriptAoVivo(opcoes));
    }

    [Fact]
    public void EnderecoPersonalizado_SoOCss_ENadaAoVivo()
    {
        var comCss = Com(o =>
        {
            o.TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado;
            o.EnderecoPersonalizado = "https://exemplo.com";
            o.CssDoEnderecoPersonalizado = "body { margin: 0; }";
        });
        var pagina = PaginaDoChat.DasOpcoes(comCss);

        Assert.Equal(ContratoComAPagina.PorOCss("body { margin: 0; }") + "\n", pagina.ScriptAoCarregar(comCss));
        Assert.Empty(pagina.ScriptsAntesDoCss(comCss));
        Assert.Null(pagina.ScriptAoVivo(comCss));

        comCss.CssDoEnderecoPersonalizado = string.Empty;
        Assert.Null(pagina.ScriptAoCarregar(comCss));
    }

    [Fact]
    public void BoasVindas_NemCssNemScript()
    {
        var opcoes = Com(o => { o.Canal = ""; o.CorDoTexto = "#FFFFFF"; o.MostrarHorario = true; });
        var pagina = PaginaDoChat.DasOpcoes(opcoes);

        Assert.Null(pagina.ScriptAoCarregar(opcoes));
        Assert.Null(pagina.ScriptAoVivo(opcoes));
        Assert.Empty(pagina.ScriptsAntesDoCss(opcoes));
    }

    // --- Extensões do chat oficial ---

    [Fact]
    public void ChatOficial_ExtensoesNaOrdem_ConformeAsOpcoes()
    {
        var opcoes = Com(o => { o.Canal = "hyfronx"; o.TipoDeChat = (int)TipoDeChat.ChatOficial; });
        var pagina = PaginaDoChat.DasOpcoes(opcoes);

        var todas = pagina.ScriptsAntesDoCss(opcoes);
        Assert.Equal(4, todas.Count);
        Assert.Contains("bttv_settings", todas[0]);
        Assert.Contains("https://cdn.betterttv.net/betterttv.js", todas[1]);
        Assert.Contains("color_normalizer", todas[2]);
        Assert.Contains("https://cdn.frankerfacez.com/static/script.min.js", todas[3]);

        opcoes.BetterTtv = false;
        Assert.Equal(2, pagina.ScriptsAntesDoCss(opcoes).Count);
        opcoes.FrankerFaceZ = false;
        Assert.Empty(pagina.ScriptsAntesDoCss(opcoes));
    }

    [Fact]
    public void OpcoesDaBetterTtv_CriamOsEmotesQuandoNaoExistem_ELigamOuDesligamO7tv()
    {
        string ligado = ChatOficialDaTwitch.OpcoesDaBetterTtv(emotesDo7tv: true, menuDeEmotes: true);
        Assert.Contains("if (!Array.isArray(opcoes.emotes)) opcoes.emotes = [39, 0];", ligado);
        Assert.Contains("opcoes.emotes[0] = opcoes.emotes[0] | 16;", ligado);
        Assert.Contains("opcoes.emoteMenu = 2;", ligado);

        string desligado = ChatOficialDaTwitch.OpcoesDaBetterTtv(emotesDo7tv: false, menuDeEmotes: false);
        Assert.Contains("opcoes.emotes[0] = opcoes.emotes[0] & ~16;", desligado);
        Assert.Contains("opcoes.emoteMenu = 0;", desligado);
    }

    [Theory]
    [InlineData("https://id.twitch.tv/oauth2/authorize?x=1", true)]
    [InlineData("https://passport.twitch.tv/login", true)]
    [InlineData("https://www.twitch.tv/login?popup=true", true)]
    [InlineData("https://twitch.tv/signup", true)]
    [InlineData("https://www.twitch.tv/hyfronx", false)]
    [InlineData("https://example.com/login", false)]
    [InlineData("não é endereço", false)]
    [InlineData(null, false)]
    public void PaginaDeLoginDaTwitch_AbreDentroDoApp(string? endereco, bool login)
    {
        Assert.Equal(login, NavegadorDoChat.EhLoginDaTwitch(endereco));
    }

    [Fact]
    public void Multiplataforma_SemTwitchComOutroCanal_AbreOPadraoSemCanal()
    {
        PaginaDoChat soKick = PaginaDoChat.DasOpcoes(Com(o => { o.ChatMultiplataforma = true; o.CanalDaKick = "gaules"; }));
        PaginaDoChat soYouTube = PaginaDoChat.DasOpcoes(Com(o => { o.ChatMultiplataforma = true; o.CanalDoYouTube = "@hyfronx"; }));

        Assert.IsType<ChatPadrao>(soKick);
        Assert.Null(soKick.Canal); // sem o ponto da Twitch
        Assert.Contains("chat.html?canal=&", soKick.Endereco);
        Assert.IsType<ChatPadrao>(soYouTube);
    }

    [Fact]
    public void SemTwitch_OutroCanalSoValeNoMultiplataforma()
    {
        Assert.IsType<PaginaDeBoasVindas>(PaginaDoChat.DasOpcoes(Com(o => o.CanalDaKick = "gaules")));
        Assert.IsType<PaginaDeBoasVindas>(PaginaDoChat.DasOpcoes(Com(o => o.ChatMultiplataforma = true)));
    }
}
