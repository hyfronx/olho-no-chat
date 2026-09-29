using System.Globalization;
using System.Text.Json;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Chat;

/// <summary>
/// O que o app diz às páginas do chat (scripts) e o que elas dizem ao app (mensagens de texto). A página do Padrão
/// (browser/chat.js) depende destes nomes e formatos exatos: <c>window.oncChat.start/apply/addAction/health</c>,
/// <c>window.oncSetScrollMode</c>, o JSON de configurações e as mensagens <c>onc:...</c>.
/// </summary>
public static class ContratoComAPagina
{
    /// <summary>O elemento com o CSS do app, criado na primeira vez e depois só atualizado.</summary>
    public const string IdDoEstilo = "onc-custom-css";

    /// <summary>
    /// As opções que a página do Padrão lê a cada mensagem (filtros, som, "Apagar mensagens antigas"...), no mesmo
    /// formato em <c>start</c> e <c>apply</c>.
    /// </summary>
    public static string JsonDeConfiguracoes(Opcoes opcoes)
    {
        // "Apagar mensagens antigas": segundos até sumir; 0 = nunca
        int fade = opcoes.ApagarMensagensAntigas && int.TryParse(opcoes.SegundosParaApagar, out int segundos) && segundos > 0
            ? segundos
            : 0;

        return JsonSerializer.Serialize(new
        {
            fade,
            hideBots = opcoes.EsconderBots,
            hideGifs = opcoes.EsconderGifs,
            hideOtherChannels = opcoes.EsconderOutrosCanais,
            highlightUsers = opcoes.DestacarUsuarios,
            allowedUsersOnly = opcoes.SoUsuariosDaLista,
            playSound = !string.Equals(opcoes.SomDeMensagem, "none", StringComparison.OrdinalIgnoreCase),
            filterAllowAllVIPs = opcoes.DestacarVips,
            filterAllowAllMods = opcoes.DestacarModeradores,
            // "vips" é a lista de usuários dos filtros, apesar do nome
            vips = opcoes.ListaDeUsuarios.Select(nome => nome.ToLowerInvariant()),
            blockList = opcoes.UsuariosBloqueados.Select(nome => nome.ToLowerInvariant()),
        });
    }

    /// <summary>Põe o CSS no elemento do app (criado se ainda não existe).</summary>
    public static string PorOCss(string css) => $$"""
        (function (css) {
            var estilo = document.getElementById('{{IdDoEstilo}}');
            if (!estilo) {
                estilo = document.createElement('style');
                estilo.id = '{{IdDoEstilo}}';
                document.head.appendChild(estilo);
            }
            estilo.textContent = css;
        })({{JsonSerializer.Serialize(css)}});
        """;

    /// <summary>Depois de cada carregamento: a página aplica as opções e conecta ao chat (uma vez só).</summary>
    public static string Iniciar(Opcoes opcoes) =>
        $"window.oncChat && window.oncChat.start({JsonDeConfiguracoes(opcoes)});";

    /// <summary>
    /// "Salvar" sem recarregar: as opções novas e o CSS novo na página aberta. O resultado é <c>true</c>, ou
    /// <c>false</c> quando o script da página não está rodando (aí o app recarrega).
    /// </summary>
    public static string AplicarAoVivo(Opcoes opcoes, string css) => $$"""
        (function (opcoes, css) {
            if (!window.oncChat) return false;
            window.oncChat.apply(opcoes);
            var estilo = document.getElementById('{{IdDoEstilo}}');
            if (!estilo) {
                estilo = document.createElement('style');
                estilo.id = '{{IdDoEstilo}}';
                document.head.appendChild(estilo);
            }
            estilo.textContent = css;
            return true;
        })({{JsonDeConfiguracoes(opcoes)}}, {{JsonSerializer.Serialize(css)}});
        """;

    /// <summary>Uma linha de ação (como /me), na cor dada: os resgates de pontos do canal.</summary>
    public static string AdicionarAcao(string nome, string cor, string texto) =>
        $"window.oncChat && window.oncChat.addAction({JsonSerializer.Serialize(nome)}, {JsonSerializer.Serialize(cor)}, " +
        $"{JsonSerializer.Serialize(texto)});";

    /// <summary>Para a vigia: a saúde da conexão da página com a Twitch.</summary>
    public const string PerguntarSaude = """
        (function () {
            try {
                return window.oncChat ? window.oncChat.health() : 'missing';
            } catch (e) {
                return 'error:' + e.message;
            }
        })();
        """;

    /// <summary>A resposta de <see cref="PerguntarSaude"/>, como o WebView2 a devolve (um texto em JSON).</summary>
    public static SaudeDaPagina LerSaude(string? respostaEmJson)
    {
        string? resposta;
        try
        {
            resposta = respostaEmJson == null ? null : JsonSerializer.Deserialize<string>(respostaEmJson);
        }
        catch (JsonException)
        {
            resposta = null;
        }

        if (resposta == null || resposta == "missing")
            return SaudeDaPagina.SemScript;
        if (resposta == "disconnected")
            return SaudeDaPagina.Reconectando;
        if (resposta.StartsWith("open:", StringComparison.Ordinal))
        {
            return double.TryParse(resposta.AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                ? SaudeDaPagina.Aberta(TimeSpan.FromMilliseconds(ms))
                : SaudeDaPagina.Aberta(TimeSpan.Zero);
        }
        return SaudeDaPagina.ComErro(resposta);
    }

    /// <summary>
    /// O modo rolagem na página do Padrão. Fica também em <c>oncScrollModeWanted</c>, que a página lê quando o script
    /// dela roda depois deste.
    /// </summary>
    /// <param name="ligado">A janela aceita cliques.</param>
    /// <param name="aviso">Mostrar o aviso do modo rolagem (com as bordas escondidas).</param>
    /// <param name="atalho">O atalho do modo rolagem em texto, ou vazio.</param>
    public static string ModoRolagem(bool ligado, bool aviso, string atalho)
    {
        string modo = JsonSerializer.Serialize(new { enabled = ligado, banner = aviso, hotkey = atalho });
        return $"window.oncScrollModeWanted = {modo}; if (window.oncSetScrollMode) window.oncSetScrollMode(window.oncScrollModeWanted);";
    }

    public const string IdDoEstiloSemRolagem = "onc-sem-rolagem";

    /// <summary>
    /// Sem as bordas, nenhuma barra de rolagem aparece, em qualquer página (a da Twitch, a do Padrão no modo rolagem, a de
    /// um endereço personalizado): um estilo à parte, posto e tirado. A rodinha do mouse continua rolando.
    /// </summary>
    public static string BarrasDeRolagem(bool visiveis) => visiveis
        ? $"(function () {{ var estilo = document.getElementById('{IdDoEstiloSemRolagem}'); if (estilo) estilo.remove(); }})();"
        : $$"""
            (function () {
                if (document.getElementById('{{IdDoEstiloSemRolagem}}')) return;
                var estilo = document.createElement('style');
                estilo.id = '{{IdDoEstiloSemRolagem}}';
                estilo.textContent = '* { scrollbar-width: none !important; } *::-webkit-scrollbar { display: none !important; }';
                (document.head || document.documentElement).appendChild(estilo);
            })();
            """;

    /// <summary>Os links do Padrão só são clicáveis com as bordas visíveis.</summary>
    public static string LinksClicaveis(bool clicaveis) =>
        $"document.body && document.body.classList.toggle('onc-links-on', {(clicaveis ? "true" : "false")});";

    /// <summary>Chat oficial: mostra a caixa de digitar da Twitch e põe o cursor nela.</summary>
    public const string AbrirCaixaDaTwitch =
        "(function () { document.body.classList.add('onc-writing'); var caixa = document.querySelector('[data-a-target=\"chat-input\"]'); if (caixa) caixa.focus(); })();";

    /// <summary>Chat oficial: esconde de novo a caixa de digitar da Twitch.</summary>
    public const string FecharCaixaDaTwitch = "document.body && document.body.classList.remove('onc-writing');";

    /// <summary>
    /// O que uma mensagem de texto da página quer dizer (<c>chrome.webview.postMessage</c>). Uma mensagem que não é
    /// texto chega como null.
    /// </summary>
    public static MensagemDaPagina LerMensagem(string? mensagem)
    {
        const string estado = "onc:chat-state:";
        return mensagem switch
        {
            "onc:play-sound" => MensagemDaPagina.TocarSom,
            "onc:exit-scroll-mode" => MensagemDaPagina.SairDoModoRolagem,
            "onc:compose-sent" => MensagemDaPagina.EscritaEnviada,
            "onc:compose-cancel" => MensagemDaPagina.EscritaCancelada,
            // "unavailable" chega como "disconnected"; qualquer outro estado conta como conectando
            _ when mensagem != null && mensagem.StartsWith(estado, StringComparison.Ordinal) => mensagem[estado.Length..] switch
            {
                "connected" => MensagemDaPagina.Conectado,
                "disconnected" => MensagemDaPagina.Desconectado,
                _ => MensagemDaPagina.Conectando,
            },
            _ => MensagemDaPagina.Desconhecida,
        };
    }
}
