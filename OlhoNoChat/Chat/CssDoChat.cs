#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Chat;

/// <summary>
/// O CSS que o app põe na página do chat (no <c>&lt;style id="onc-custom-css"&gt;</c>): o visual das mensagens do Padrão,
/// as cores de destaque dos filtros, as opções de "Texto das mensagens" e o visual do chat oficial, que imita o Padrão.
/// Só regras que pegam em algum elemento das páginas (decisão 14).
/// </summary>
public static class CssDoChat
{
    // O visual das mensagens do Padrão (o tema dá a fonte, em negrito). O chat oficial usa os mesmos números, para o
    // mesmo tamanho do texto dar letras iguais nos dois.
    public const string FonteDoTema = "\"Helvetica Neue\", Helvetica, Arial, sans-serif";
    public const int TamanhoDaLetraPx = 18;
    public const int AlturaDaLinhaPx = 24;      // entre as linhas de uma mensagem
    public const int EspacoDaMensagemPx = 6;    // em cima e embaixo de cada mensagem

    // "Mostrar o horário das mensagens" (Padrão e chat oficial)
    private const string VisualDoHorario =
        "display: inline !important; color: #A8A8A8 !important; font-size: 15px !important; font-weight: 600 !important; " +
        "letter-spacing: 0 !important; margin-right: 6px !important; padding: 0 !important;";

    // "Contorno das letras" = "Sombra suave"
    private const string SombraSuave = "0 1px 3px rgba(0,0,0,.95), 0 0 2px rgba(0,0,0,.8)";

    /// <summary>
    /// O CSS do chat Padrão: o do app (tema "Padrão", ou "Nenhum" sem CSS próprio) ou só o CSS próprio do tema "Nenhum";
    /// nos dois casos, com as opções de "Texto das mensagens".
    /// </summary>
    public static string DoChatPadrao(Opcoes opcoes)
    {
        // Com o CSS próprio, as cores de destaque dos Filtros não entram: vale o que a pessoa escreveu
        string css = opcoes.Tema == Opcoes.TemaNenhum && opcoes.CssDoTemaNenhum.Length > 0
            ? opcoes.CssDoTemaNenhum
            : VisualDoPadrao(opcoes);
        return css + TextoDasMensagensNoPadrao(opcoes);
    }

    // Os seletores são fracos de propósito: o chat.css da página ganha deles onde precisa (o recuo da barra do chat
    // unido em ".chat_line.onc-other-channel", a margem zero de ".onc-stack .onc-overlay")
    private static string VisualDoPadrao(Opcoes opcoes) => $$"""
        /* Linhas de uma mensagem mais juntas e espaço entre as mensagens, sem espaço dos lados */
        .chat_line {
            line-height: {{AlturaDaLinhaPx}}px;
            padding: {{EspacoDaMensagemPx}}px 0 !important;
        }

        /* Emotes apoiados na base do texto, como as letras; a margem de cima negativa não deixa um emote alto
           aumentar a linha */
        .emoticon {
            max-height: 28px;
            margin: -4px -2px 0 !important;
            vertical-align: text-bottom !important;
        }

        /* Badges no meio das letras do nome */
        .badges img {
            width: 20px;
            height: 20px;
            vertical-align: middle;
            margin-bottom: 2px;
        }

        /* O nome e a mensagem correm como um texto só */
        .message {
            display: inline !important;
            vertical-align: middle;
        }

        /* Cores de destaque dos Filtros do chat */
        .highlight { background-color: {{Rgba(opcoes.CorDoDestaque)}} !important; }
        .highlightMod { background-color: {{Rgba(opcoes.CorDosModeradores)}} !important; }
        .highlightVIP { background-color: {{Rgba(opcoes.CorDosVips)}} !important; }
        """;

    // As regras do tema (browser/chat.css) são mais específicas: tudo aqui precisa de !important
    private static string TextoDasMensagensNoPadrao(Opcoes opcoes)
    {
        const string linhas = "#chat_box .chat_line, #chat_box .chat_line .nick, #chat_box .chat_line .message";
        var css = new StringBuilder();

        // Só o texto: o nome fica na cor da Twitch, e as linhas /me e de resgate (a página pinta o texto delas) também
        if (CorValida(opcoes.CorDoTexto))
            css.Append($"\n#chat_box .chat_line .message:not([style*=\"color\"]) {{ color: {opcoes.CorDoTexto} !important; }}");

        string? sombra = Sombra(opcoes);
        if (sombra != null)
            css.Append($"\n#chat_box, {linhas} {{ text-shadow: {sombra} !important; }}");

        string? fonte = FonteEscolhida(opcoes);
        if (fonte != null)
            css.Append($"\n#chat_box, {linhas} {{ font-family: '{fonte}', sans-serif !important; letter-spacing: normal !important; }}");

        // A página escreve a hora em toda linha e a esconde
        if (opcoes.MostrarHorario)
            css.Append($"\n#chat_box .chat_line .time_stamp {{ {VisualDoHorario} }}");

        return css.ToString();
    }

    /// <summary>
    /// O CSS do chat oficial: a aparência do app com o visual das mensagens do Padrão, ou o CSS próprio; depois, qual
    /// caixa de digitar aparece e o horário.
    /// </summary>
    public static string DoChatOficial(Opcoes opcoes)
    {
        var css = new StringBuilder(opcoes.AparenciaPadraoNoChatOficial
            ? PadraoDoChatOficial + "\n" + MensagensDoChatOficial(opcoes)
            : opcoes.CssDoChatOficial);

        // Uma caixa de digitar por vez: a da Twitch só enquanto a escrita está aberta (o app põe "onc-writing" no body)
        css.Append(opcoes.CaixaDaTwitch
            ? "\nbody:not(.onc-writing) .chat-input { display: none !important; }"
            : "\n.chat-input { display: none !important; }");

        // O horário é o do app (script do chat oficial); o da Twitch ficaria repetido
        css.Append(opcoes.MostrarHorario
            ? $"\n.chat-line__message .onc-time {{ {VisualDoHorario} }}\n.chat-line__timestamp {{ display: none !important; }}"
            : "\n.onc-time { display: none !important; }");

        return css.ToString();
    }

    // Cada mensagem da Twitch é um ".chat-line__message", com a FrankerFaceZ ou sem ela
    private static string MensagensDoChatOficial(Opcoes opcoes)
    {
        // A fonte do tema tem 1 px entre as letras; uma fonte escolhida fica com o espaço dela (como no Padrão)
        string? escolhida = FonteEscolhida(opcoes);
        string fonte = escolhida != null ? $"'{escolhida}', sans-serif" : FonteDoTema;
        string entreAsLetras = escolhida != null ? "normal" : "1px";

        var css = new StringBuilder($$"""
            .chat-line__message {
                font-family: {{fonte}} !important;
                font-size: {{TamanhoDaLetraPx}}px !important;
                font-weight: 700 !important;
                letter-spacing: {{entreAsLetras}} !important;
                line-height: {{AlturaDaLinhaPx}}px !important;
                padding-top: {{EspacoDaMensagemPx}}px !important;
                padding-bottom: {{EspacoDaMensagemPx}}px !important;
            }
            .chat-line__message .text-fragment, .chat-line__message .mention-fragment, .chat-line__message .message,
            .chat-line__message .chat-author__display-name, .chat-line__message [data-a-target="chat-line-message-body"] {
                font: inherit !important;
            }
            .chat-line__message .chat-image, .chat-line__message .ffz-emote, .chat-line__message .emoji, .chat-line__message .ffz-emoji {
                margin-top: -4px !important;
                margin-bottom: 0 !important;
                vertical-align: text-bottom !important;
            }
            """);

        // Só o texto muda de cor: os nomes ficam com a cor da Twitch
        if (CorValida(opcoes.CorDoTexto))
            css.Append($"\n.chat-line__message .text-fragment {{ color: {opcoes.CorDoTexto} !important; }}");

        // "Contorno preto" é o contorno da aparência do app
        string? sombra = Sombra(opcoes);
        if (sombra != null)
            css.Append($"\n.chat-line__message, .chat-line__message * {{ text-shadow: {sombra} !important; }}");

        return css.ToString();
    }

    /// <summary>
    /// A aparência do app no chat oficial (também mostrada, somente leitura, no editor da aba Aparência): a página da
    /// Twitch transparente sobre o fundo do app, com contorno nas letras e só o que interessa do chat.
    /// </summary>
    public const string PadraoDoChatOficial = """
        /* Fundo transparente: quem escurece o chat é o "Fundo" da barra laranja */
        body, .tw-root, .tw-root--theme-dark, .tw-flex, .stream-chat, .chat-room, .chat-list, .scrollable-area {
            background-color: transparent !important;
        }
        .scrollable-area { color: white !important; }

        /* Contorno preto de 1 px nas letras */
        body, p, span, div, a, h1, h2, h3 {
            text-shadow: -1px -1px 0 rgba(0, 0, 0, .85), 1px -1px 0 rgba(0, 0, 0, .85), -1px 1px 0 rgba(0, 0, 0, .85), 1px 1px 0 rgba(0, 0, 0, .85);
        }

        /* Sem o cabeçalho do chat e o placar de quem mais deu presentes */
        .stream-chat .stream-chat-header { display: none; background-color: transparent !important; color: white !important; }
        .tw-z-default { display: none; }
        div:has(> div > div[aria-label="Expand Top Gifters Leaderboard"]) { display: none !important; }

        .chat-line__timestamp { color: gray !important; }
        #chat-room-header-label { color: #cacaca !important; }

        /* Hype train e parecidos, anúncios, e blocos de ícone + texto */
        .community-highlight { background-color: rgba(0, 0, 0, .75) !important; }
        .announcement-line { background-color: rgba(0, 0, 0, .2) !important; }
        div:has(> div.tw-svg + div) { background-color: transparent !important; }

        /* Caixa de digitar da Twitch */
        .chat-input-tray__open, .chat-input-container__open { background-color: transparent !important; color: white !important; }
        .chat-wysiwyg-input__placeholder { color: #a9a9a9 !important; }
        .font-scale--default:has([data-a-target="chat-input"]) { background-color: rgba(0, 0, 0, .25) !important; }

        /* Tema claro da Twitch com cara de escuro */
        .tw-root--theme-light { background-color: transparent !important; color: white !important; }
        .tw-root--theme-light svg { fill: white !important; }
        .tw-root--theme-light input, .tw-root--theme-light textarea { color: white !important; }
        .tw-root--theme-light input::placeholder, .tw-root--theme-light textarea::placeholder { color: #cacaca !important; }
        .tw-root--theme-light [class*="tw-border-"] { border-color: rgba(255, 255, 255, .25) !important; }

        /* Botões, avisos, placar e destaques têm fundo próprio: o contorno só deixaria as letras pequenas pesadas */
        button, button *, [role="button"], [role="button"] *,
        .tw-callout-message, .tw-callout-message *, [class*="ScCallout"] *,
        [class*="channelLeaderboard"] *, .community-highlight * {
            text-shadow: none !important;
        }
        """;

    /// <summary>
    /// O CSS com que a caixa do tema "Nenhum" começa (vazia): letras brancas em negrito de 16 px com sombra, e o destaque
    /// amarelo. Para a pessoa editar a partir dele.
    /// </summary>
    public const string ExemploDoTemaNenhum = """
        #chat_box {
            letter-spacing: 1px;
            text-shadow: 2px 2px 0 #000, 2px 2px 4px #000;
        }

        .chat_line {
            color: #fff;
            font-weight: bold;
            font-size: 16px !important;
        }

        .message { display: inline !important; }

        .highlight { background-color: rgba(255, 255, 0, 0.5) !important; }
        """;

    /// <summary>A cor em rgba(), com ponto decimal em qualquer idioma do Windows ("0.59", nunca "0,59").</summary>
    public static string Rgba(Color cor) =>
        string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.00})", cor.R, cor.G, cor.B, cor.A / 255f);

    private static bool CorValida(string? cor) => cor != null && Regex.IsMatch(cor, "^#[0-9A-Fa-f]{6}$");

    // "theme" (o contorno do tema) não muda nada
    private static string? Sombra(Opcoes opcoes) => opcoes.ContornoDasLetras switch
    {
        "soft" => SombraSuave,
        "none" => "none",
        _ => null,
    };

    // "theme" = a fonte do tema
    private static string? FonteEscolhida(Opcoes opcoes) =>
        opcoes.Fonte is "Segoe UI" or "Arial" or "Verdana" ? opcoes.Fonte : null;
}
