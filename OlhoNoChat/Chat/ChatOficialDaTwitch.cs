using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Chat;

/// <summary>
/// O chat oficial: a página popout da Twitch (que se conecta sozinha), com as extensões de emotes escolhidas, o CSS do
/// app e um script que põe o horário nas mensagens e avisa o app sobre a caixa de digitar da Twitch.
/// </summary>
public sealed class ChatOficialDaTwitch : PaginaDoChat
{
    private const string ScriptDaBetterTtv = "https://cdn.betterttv.net/betterttv.js";
    private const string ScriptDaFrankerFaceZ = "https://cdn.frankerfacez.com/static/script.min.js";

    // Opções "emotes" da BetterTTV: os bits ligados por padrão nela (emotes da BTTV, animados, da FFZ e modificadores)
    // e o bit dos emotes do 7TV
    private const int EmotesPadraoDaBetterTtv = 1 | 2 | 4 | 32;
    private const int EmotesDo7tv = 16;

    private readonly string _canal;

    /// <summary>A chave depende também das extensões e da aparência, que a página só lê ao abrir.</summary>
    public ChatOficialDaTwitch(string canal, Opcoes opcoes)
        : base($"https://www.twitch.tv/popout/{canal}/chat?popout=",
               string.Join("|", "oficial", canal, opcoes.BetterTtv, opcoes.Emotes7tv, opcoes.MenuDeEmotesDoBetterTtv,
                   opcoes.FrankerFaceZ, opcoes.AparenciaPadraoNoChatOficial, opcoes.CssDoChatOficial))
    {
        _canal = canal;
    }

    public override string? Canal => _canal;

    public override IReadOnlyList<string> ScriptsAntesDoCss(Opcoes opcoes)
    {
        var scripts = new List<string>();
        if (opcoes.BetterTtv)
        {
            scripts.Add(OpcoesDaBetterTtv(opcoes.Emotes7tv, opcoes.MenuDeEmotesDoBetterTtv));
            scripts.Add(CarregarScript(ScriptDaBetterTtv));
        }
        if (opcoes.FrankerFaceZ)
        {
            scripts.Add(TirarOCorretorDeCoresDaFrankerFaceZ);
            scripts.Add(CarregarScript(ScriptDaFrankerFaceZ));
        }
        return scripts;
    }

    /// <summary>
    /// As opções da BetterTTV ficam no localStorage da página (<c>bttv_settings</c>) e são gravadas a cada
    /// carregamento, para desligar também funcionar. "emotes" é [bits ligados, bits mudados]; quando a BetterTTV ainda
    /// não gravou as opções dela, o app cria com os padrões dela (decisão 13), senão o 7TV só valeria no carregamento
    /// seguinte. O menu de emotes: 2 = o moderno, 0 = desligado.
    /// </summary>
    public static string OpcoesDaBetterTtv(bool emotesDo7tv, bool menuDeEmotes) => $$"""
        (function () {
            try {
                var opcoes = JSON.parse(localStorage.getItem('bttv_settings') || 'null') || {};
                if (!Array.isArray(opcoes.emotes)) opcoes.emotes = [{{EmotesPadraoDaBetterTtv}}, 0];
                opcoes.emotes[0] = {{(emotesDo7tv ? $"opcoes.emotes[0] | {EmotesDo7tv}" : $"opcoes.emotes[0] & ~{EmotesDo7tv}")}};
                opcoes.emoteMenu = {{(menuDeEmotes ? 2 : 0)}};
                localStorage.setItem('bttv_settings', JSON.stringify(opcoes));
            } catch (e) {
                console.error('Olho no Chat: não deu para gravar as opções da BetterTTV', e);
            }
        })();
        """;

    // A folha de estilo "color_normalizer" que a FrankerFaceZ põe no <head> pinta o fundo e acaba com a transparência:
    // sai assim que entra
    private const string TirarOCorretorDeCoresDaFrankerFaceZ = """
        (function () {
            new MutationObserver(function (mudancas) {
                mudancas.forEach(function (mudanca) {
                    mudanca.addedNodes.forEach(function (no) {
                        if (no.tagName === 'LINK' && (no.href || '').indexOf('color_normalizer') >= 0) no.remove();
                    });
                });
            }).observe(document.head, { childList: true });
        })();
        """;

    private static string CarregarScript(string endereco) =>
        $"(function (endereco) {{ var script = document.createElement('script'); script.src = endereco; document.head.appendChild(script); }})('{endereco}');";

    public override string Css(Opcoes opcoes) => CssDoChat.DoChatOficial(opcoes);

    // O horário de chegada em cada mensagem nova (aparece só com "Mostrar o horário das mensagens"), e a caixa de digitar
    // da Twitch: Esc, ou Enter com a caixa vazia depois (a mensagem foi), avisam o app para devolver o foco ao jogo
    public override string Script(Opcoes opcoes) => """
        (function () {
            if (!window.oncHorario) {
                window.oncHorario = true;
                var marcar = function (linha) {
                    if (linha.querySelector('.onc-time')) return;
                    var hora = document.createElement('span');
                    hora.className = 'onc-time';
                    hora.textContent = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
                    var antes = linha.querySelector('.chat-line__message--badges, .chat-line__username-container, .chat-line__username');
                    if (antes) antes.parentNode.insertBefore(hora, antes); else linha.prepend(hora);
                };
                new MutationObserver(function (mudancas) {
                    mudancas.forEach(function (mudanca) {
                        mudanca.addedNodes.forEach(function (no) {
                            if (no.nodeType !== 1) return;
                            if (no.matches('.chat-line__message')) marcar(no);
                            else no.querySelectorAll('.chat-line__message').forEach(marcar);
                        });
                    });
                }).observe(document.body, { childList: true, subtree: true });
            }

            if (window.oncCaixaDaTwitch) return;
            window.oncCaixaDaTwitch = true;
            document.addEventListener('keydown', function (e) {
                var caixa = e.target && e.target.closest && e.target.closest('[data-a-target="chat-input"]');
                if (!caixa || !window.chrome || !window.chrome.webview) return;
                if (e.key === 'Escape') {
                    window.chrome.webview.postMessage('onc:compose-cancel');
                } else if (e.key === 'Enter' && !e.shiftKey) {
                    setTimeout(function () {
                        if (!caixa.innerText.trim()) window.chrome.webview.postMessage('onc:compose-sent');
                    }, 250);
                }
            }, true);
        })();
        """;

    // Só o CSS do app muda ao vivo (texto das mensagens, horário, qual caixa de digitar aparece)
    public override string ScriptAoVivo(Opcoes opcoes) => ContratoComAPagina.PorOCss(Css(opcoes));
}
