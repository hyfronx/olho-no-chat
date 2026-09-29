using System.Globalization;
using System.Windows.Media;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Chat;

/// <summary>O CSS que o app põe no chat, com cada opção de Aparência e dos Filtros.</summary>
public sealed class CssDoChatTestes
{
    [Fact]
    public void Rgba_PontoDecimalEmQualquerIdioma()
    {
        CultureInfo antes = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("rgba(245,245,0,0.59)", CssDoChat.Rgba(Color.FromArgb(150, 245, 245, 0)));
            Assert.Equal("rgba(1,2,3,1.00)", CssDoChat.Rgba(Color.FromArgb(255, 1, 2, 3)));

            string css = CssDoChat.DoChatPadrao(new Opcoes());
            Assert.Contains(".highlight { background-color: rgba(245,245,0,0.59) !important; }", css);
            Assert.Contains(".highlightMod { background-color: rgba(0,173,3,0.59) !important; }", css);
            Assert.Contains(".highlightVIP { background-color: rgba(219,51,179,0.59) !important; }", css);
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Padrao_OVisualDoApp_SoComRegrasQueAPaginaGera()
    {
        string css = CssDoChat.DoChatPadrao(new Opcoes());

        Assert.Contains("line-height: 24px;", css);
        Assert.Contains("padding: 6px 0 !important;", css);
        Assert.Contains("max-height: 28px;", css);
        Assert.Contains(".badges img", css);
        // Decisão 14: a página não gera img.emoji nem .username
        Assert.DoesNotContain("img.emoji", css);
        Assert.DoesNotContain(".username", css);
    }

    [Fact]
    public void Padrao_SeletoresFracos_OChatCssGanhaOndePrecisa()
    {
        // O recuo da barra do chat unido (.chat_line.onc-other-channel) e a margem zero dos emotes por cima
        // (.onc-stack .onc-overlay) só ganham se o app usar .chat_line e .emoticon sozinhos
        string css = CssDoChat.DoChatPadrao(new Opcoes());

        Assert.Contains("\n.chat_line {", "\n" + css);
        Assert.Contains("\n.emoticon {", css);
        Assert.DoesNotContain("#chat_box .chat_line {", css);
    }

    [Fact]
    public void Padrao_TemaNenhumComCssProprio_SoOCssProprioMaisOTexto()
    {
        var opcoes = new Opcoes { Tema = Opcoes.TemaNenhum, CssDoTemaNenhum = "#chat_box { color: red; }", MostrarHorario = true };
        string css = CssDoChat.DoChatPadrao(opcoes);

        Assert.StartsWith("#chat_box { color: red; }", css);
        Assert.DoesNotContain(".highlight", css);
        Assert.Contains(".time_stamp", css);
    }

    [Fact]
    public void Padrao_TemaNenhumSemCssProprio_OVisualDoApp()
    {
        Assert.Equal(CssDoChat.DoChatPadrao(new Opcoes()), CssDoChat.DoChatPadrao(new Opcoes { Tema = Opcoes.TemaNenhum }));
    }

    [Fact]
    public void Padrao_TextoDasMensagens()
    {
        string padrao = CssDoChat.DoChatPadrao(new Opcoes { ContornoDasLetras = "theme" });
        Assert.DoesNotContain("text-shadow", padrao);
        Assert.DoesNotContain("font-family", padrao);
        Assert.DoesNotContain(".time_stamp", padrao);

        string css = CssDoChat.DoChatPadrao(new Opcoes { CorDoTexto = "#B8F5B0", ContornoDasLetras = "soft", Fonte = "Segoe UI", MostrarHorario = true });
        Assert.Contains("#chat_box .chat_line .message:not([style*=\"color\"]) { color: #B8F5B0 !important; }", css);
        Assert.Contains("#chat_box, #chat_box .chat_line, #chat_box .chat_line .nick, #chat_box .chat_line .message { text-shadow: 0 1px 3px rgba(0,0,0,.95), 0 0 2px rgba(0,0,0,.8) !important; }", css);
        Assert.Contains("font-family: 'Segoe UI', sans-serif !important; letter-spacing: normal !important;", css);
        Assert.Contains("#chat_box .chat_line .time_stamp { display: inline !important; color: #A8A8A8 !important; font-size: 15px !important;", css);

        // "Nenhum" (o padrão da primeira instalação) tira até o contorno do tema
        Assert.Contains("text-shadow: none !important;", CssDoChat.DoChatPadrao(new Opcoes()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("branco")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    public void CorInvalida_NaoEntra(string cor)
    {
        Assert.DoesNotContain("color: " + cor + " !important", CssDoChat.DoChatPadrao(new Opcoes { CorDoTexto = cor }));
        Assert.DoesNotContain(".text-fragment { color", CssDoChat.DoChatOficial(new Opcoes { CorDoTexto = cor }));
    }

    [Fact]
    public void ChatOficial_AparenciaDoApp_ComOVisualDoPadrao()
    {
        string css = CssDoChat.DoChatOficial(new Opcoes());

        Assert.StartsWith(CssDoChat.PadraoDoChatOficial, css);
        Assert.Contains("font-family: \"Helvetica Neue\", Helvetica, Arial, sans-serif !important;", css);
        Assert.Contains("font-size: 18px !important;", css);
        Assert.Contains("letter-spacing: 1px !important;", css);
        Assert.Contains("line-height: 24px !important;", css);
        Assert.Contains(".chat-line__message, .chat-line__message * { text-shadow: none !important; }", css);
        // Caixa do app (a da Twitch escondida) e sem horário
        Assert.Contains("\n.chat-input { display: none !important; }", css);
        Assert.Contains("\n.onc-time { display: none !important; }", css);
    }

    [Fact]
    public void ChatOficial_CssProprio_SemOVisualDoPadrao()
    {
        var opcoes = new Opcoes { AparenciaPadraoNoChatOficial = false, CssDoChatOficial = "body { color: red; }", Fonte = "Arial" };
        string css = CssDoChat.DoChatOficial(opcoes);

        Assert.StartsWith("body { color: red; }", css);
        Assert.DoesNotContain(".chat-line__message {", css);
        Assert.DoesNotContain(CssDoChat.PadraoDoChatOficial, css);

        opcoes.CssDoChatOficial = string.Empty;
        opcoes.EsconderPlacarDoTopo = false;
        opcoes.EsconderTituloDoChat = false;
        Assert.StartsWith("\n.chat-input", CssDoChat.DoChatOficial(opcoes));
    }

    [Fact]
    public void ChatOficial_PartesDaPaginaEscondidasComQualquerAparencia()
    {
        static string Regra(string seletor) => seletor + " { display: none !important; }";
        // Padrão: título e placar somem, os destaques ficam
        string padrao = CssDoChat.DoChatOficial(new Opcoes());
        Assert.Contains(Regra(CssDoChat.TituloDoChat), padrao);
        Assert.Contains(Regra(CssDoChat.PlacarDoTopo), padrao);
        Assert.DoesNotContain(Regra(CssDoChat.DestaquesDoChat), padrao);

        string proprio = CssDoChat.DoChatOficial(new Opcoes { AparenciaPadraoNoChatOficial = false, CssDoChatOficial = "x { }", EsconderDestaques = true });
        Assert.Contains(Regra(CssDoChat.TituloDoChat), proprio);
        Assert.Contains(Regra(CssDoChat.PlacarDoTopo), proprio);
        Assert.Contains(Regra(CssDoChat.DestaquesDoChat), proprio);

        string nenhum = CssDoChat.DoChatOficial(new Opcoes { EsconderTituloDoChat = false, EsconderPlacarDoTopo = false });
        Assert.DoesNotContain(CssDoChat.TituloDoChat, nenhum);
        Assert.DoesNotContain(CssDoChat.PlacarDoTopo, nenhum);
        Assert.DoesNotContain("stream-chat-header", CssDoChat.PadraoDoChatOficial);
        // Pelas partes do placar, sem depender do idioma da página (o texto dos botões muda)
        Assert.DoesNotContain("aria-label", CssDoChat.PlacarDoTopo);
        Assert.Contains("[class*=\"channelLeaderboard\"]", CssDoChat.PlacarDoTopo);
    }

    [Fact]
    public void ChatOficial_CaixaDaTwitchEHorario()
    {
        string css = CssDoChat.DoChatOficial(new Opcoes { CaixaDaTwitch = true, MostrarHorario = true, Fonte = "Verdana", CorDoTexto = "#FFFFFF" });

        Assert.Contains("body:not(.onc-writing) .chat-input { display: none !important; }", css);
        Assert.Contains(".chat-line__message .onc-time { display: inline !important;", css);
        Assert.Contains(".chat-line__timestamp { display: none !important; }", css);
        Assert.Contains("font-family: 'Verdana', sans-serif !important;", css);
        Assert.Contains("letter-spacing: normal !important;", css);
        Assert.Contains(".chat-line__message .text-fragment { color: #FFFFFF !important; }", css);
    }

    [Fact]
    public void ChatOficial_SemAsRegrasDeClassesQueATwitchNaoUsa()
    {
        // Decisão 14: nomes com erro ou que a Twitch não usa mais (conferido na página em 28/09/2026)
        Assert.DoesNotContain("chat-room__notifcations", CssDoChat.PadraoDoChatOficial);
        Assert.DoesNotContain("chat-wysiwyg-input__box", CssDoChat.PadraoDoChatOficial);
        Assert.DoesNotContain("marquee-animation", CssDoChat.PadraoDoChatOficial);
        // Conferido em 29/09/2026: o placar tem opção própria; ".tw-z-default" não existe mais na página
        Assert.DoesNotContain("Expand Top Gifters", CssDoChat.PadraoDoChatOficial);
        Assert.DoesNotContain("tw-z-default", CssDoChat.PadraoDoChatOficial);
    }

    [Fact]
    public void EnderecoPersonalizado_SoOCssDigitado()
    {
        var opcoes = new Opcoes { TipoDeChat = (int)TipoDeChat.EnderecoPersonalizado, EnderecoPersonalizado = "https://a", CssDoEnderecoPersonalizado = "x { }" };

        Assert.Equal("x { }", PaginaDoChat.DasOpcoes(opcoes).Css(opcoes));
    }
}
