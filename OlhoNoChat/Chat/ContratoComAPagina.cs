using System.Globalization;
using System.Text.Json;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Kick;
using OlhoNoChat.TikTok;
using OlhoNoChat.Twitch;
using OlhoNoChat.YouTube;

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
            // "Prefixos dos comandos": a mensagem que começa com um deles é um comando
            commandPrefixes = opcoes.PrefixosDosComandos,
            hideGifs = opcoes.EsconderGifs,
            // O selo "Primeira vez" na primeira mensagem de alguém no canal
            firstTime = opcoes.MostrarPrimeiraVez,
            hideOtherChannels = opcoes.EsconderOutrosCanais,
            // Os avisos de cada plataforma, cada um com o seu interruptor
            twitchNotices = opcoes.MostrarAvisosDaTwitch,
            youTubeNotices = opcoes.MostrarAvisosDoYouTube,
            tiktokGifts = opcoes.MostrarPresentesDoTikTok,
            tiktokFollows = opcoes.MostrarSeguidoresDoTikTok,
            tiktokShares = opcoes.MostrarCompartilhamentosDoTikTok,
            tiktokSuperFans = opcoes.MostrarSuperfasDoTikTok,
            highlightUsers = opcoes.DestacarUsuarios,
            allowedUsersOnly = opcoes.SoUsuariosDaLista,
            playSound = !string.Equals(opcoes.SomDeMensagem, "none", StringComparison.OrdinalIgnoreCase),
            filterAllowAllVIPs = opcoes.DestacarVips,
            filterAllowAllMods = opcoes.DestacarModeradores,
            // "vips" é a lista de usuários dos filtros, apesar do nome
            vips = opcoes.ListaDeUsuarios.Select(nome => nome.ToLowerInvariant()),
            blockList = opcoes.UsuariosBloqueados.Select(nome => nome.ToLowerInvariant()),
            // Chat Multiplataforma: cada mensagem mostra o ícone da plataforma
            multiplatform = opcoes.ChatMultiplataforma,
            // Pausar o chat: a velocidade de continuar em linhas por segundo (0 = no ritmo de leitura) e o atalho, que o
            // aviso da pausa mostra
            resumeSpeed = opcoes.VelocidadeAutomaticaAoContinuar ? 0 : opcoes.LinhasPorSegundoAoContinuar,
            pauseHotkey = Atalho.Existe(opcoes.AtalhoPausar) ? opcoes.AtalhoPausar.ToString() : string.Empty,
            // Overlay da live: cada linha mostrada (e cada uma tirada) vai também para o app (Overlay/ContratoDoOverlay.cs)
            overlay = opcoes.OverlayLigado,
        });
    }

    /// <summary>
    /// O botão e o atalho de pausar: pausa o chat do Padrão, ou continua o pausado. A página responde com o estado novo
    /// (<see cref="MensagemDaPagina.ChatPausado"/>, <see cref="MensagemDaPagina.ChatContinuando"/> ou
    /// <see cref="MensagemDaPagina.ChatAoVivo"/>).
    /// </summary>
    public const string AlternarPausa = "window.oncChat && window.oncChat.togglePause && window.oncChat.togglePause();";

    /// <summary>
    /// "Mensagens de teste" da aba Overlay: a página monta linhas de exemplo como as de verdade e manda só para o overlay
    /// (<c>onc:overlay:{"t":"test",...}</c>). Responde <c>false</c> com o overlay desligado.
    /// </summary>
    public const string TesteDoOverlay = "window.oncChat && window.oncChat.overlayTest && window.oncChat.overlayTest();";

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

    /// <summary>Um aviso do app no balão de estado do chat (nunca vai para o overlay da live).</summary>
    public static string AvisoDoApp(string texto) =>
        $"window.oncChat && window.oncChat.appNotice && window.oncChat.appNotice({JsonSerializer.Serialize(texto)});";

    /// <summary>Uma linha de ação (como /me), na cor dada: os resgates de pontos do canal.</summary>
    public static string AdicionarAcao(string nome, string cor, string texto) =>
        $"window.oncChat && window.oncChat.addAction({JsonSerializer.Serialize(nome)}, {JsonSerializer.Serialize(cor)}, " +
        $"{JsonSerializer.Serialize(texto)});";

    /// <summary>
    /// O cartão do AutoMod embaixo do chat do Padrão: a mais antiga das mensagens seguradas, quantas são, os atalhos e
    /// um aviso de como foi a última decisão (que fica um instante, mesmo com a fila vazia). Fila vazia e sem aviso:
    /// o cartão some. Os botões do cartão respondem com <c>onc:automod:allow:&lt;id&gt;</c> ou <c>deny</c>.
    /// </summary>
    public static string MostrarAutoMod(AutoModDoCanal autoMod, Atalho? aprovar, Atalho? recusar, string? aviso)
    {
        EventSubDaTwitch.MensagemRetida? primeira = autoMod.Primeira;
        string json = JsonSerializer.Serialize(new
        {
            current = primeira == null ? null : new
            {
                id = primeira.Id,
                login = primeira.Login.ToLowerInvariant(),
                name = primeira.Nome,
                parts = primeira.Partes.Select(p => p.IdDoEmote == null ? (object)p.Texto : new { emoteId = p.IdDoEmote, name = p.Texto }),
                reason = AutoModDoCanal.Motivo(primeira),
            },
            total = autoMod.Mensagens.Count,
            allowHotkey = Atalho.Existe(aprovar) ? aprovar.ToString() : string.Empty,
            denyHotkey = Atalho.Existe(recusar) ? recusar.ToString() : string.Empty,
            notice = aviso ?? string.Empty,
        });
        return $"window.oncChat && window.oncChat.automod && window.oncChat.automod({json});";
    }

    /// <summary>Um botão do cartão do AutoMod: o id da mensagem e se é para aprovar; null se a mensagem for outra.</summary>
    public static (string Id, bool Aprovar)? LerPedidoDoAutoMod(string? mensagem)
    {
        const string aprovar = "onc:automod:allow:", recusar = "onc:automod:deny:";
        if (mensagem?.StartsWith(aprovar, StringComparison.Ordinal) == true && mensagem.Length > aprovar.Length)
            return (mensagem[aprovar.Length..], true);
        if (mensagem?.StartsWith(recusar, StringComparison.Ordinal) == true && mensagem.Length > recusar.Length)
            return (mensagem[recusar.Length..], false);
        return null;
    }

    /// <summary>Uma mensagem do chat do YouTube (Chat Multiplataforma).</summary>
    public static string AdicionarDoYouTube(MensagemParaAPagina mensagem)
    {
        string json = JsonSerializer.Serialize(new
        {
            id = mensagem.Id,
            login = mensagem.Login,
            name = mensagem.Nome,
            role = mensagem.Papel,
            parts = mensagem.Partes,
            superchat = mensagem.SuperChat is { } sc ? new { amount = sc.Valor, color = sc.Cor } : null,
            notice = mensagem.Aviso,
        });
        return $"window.oncChat && window.oncChat.addYouTube && window.oncChat.addYouTube({json});";
    }

    /// <summary>
    /// Uma mensagem do chat do TikTok (Chat Multiplataforma): o texto e os emotes da live como
    /// <c>{ emote: { src }, name }</c>, igual aos emotes das mensagens da Twitch.
    /// </summary>
    public static string AdicionarDoTikTok(ItemDoTikTok item)
    {
        string json = JsonSerializer.Serialize(new
        {
            id = item.Id,
            login = item.Login,
            name = item.Nome,
            role = item.Papel switch
            {
                PapelNoTikTok.Dono => "owner",
                PapelNoTikTok.Moderador => "moderator",
                PapelNoTikTok.Assinante => "subscriber",
                _ => string.Empty,
            },
            parts = item.Partes.Select(p => p.Imagem == null ? (object)p.Texto : new { emote = new { src = p.Imagem }, name = "" }),
        });
        return $"window.oncChat && window.oncChat.addTikTok && window.oncChat.addTikTok({json});";
    }

    /// <summary>
    /// Um aviso do TikTok (Chat Multiplataforma), no formato dos avisos da Twitch: o texto em partes, com os valores (o
    /// presente, a quantidade) em <c>{ value }</c> e a imagem do presente em <c>{ image, name }</c>. <c>kind</c> diz qual
    /// interruptor vale para ele (a página esconde os desligados).
    /// </summary>
    public static string AdicionarAvisoDoTikTok(AvisoDoTikTok aviso)
    {
        object[] partes = aviso.Tipo switch
        {
            TipoDeAvisoDoTikTok.Presente when aviso.Presente is { } p => Presente(p),
            TipoDeAvisoDoTikTok.Seguiu => ["seguiu o criador de LIVE"],
            TipoDeAvisoDoTikTok.Compartilhou => ["compartilhou a LIVE"],
            _ => ["acabou de se tornar ", new { value = "Superfã" }, "!"],
        };
        string json = JsonSerializer.Serialize(new
        {
            id = aviso.Id,
            login = aviso.Login,
            name = aviso.Nome,
            role = string.Empty,
            kind = aviso.Tipo switch
            {
                TipoDeAvisoDoTikTok.Presente => "gift",
                TipoDeAvisoDoTikTok.Seguiu => "follow",
                TipoDeAvisoDoTikTok.Compartilhou => "share",
                _ => "superfan",
            },
            parts = Array.Empty<object>(),
            notice = partes,
        });
        return $"window.oncChat && window.oncChat.addTikTok && window.oncChat.addTikTok({json});";
    }

    // "enviou 5x [imagem] Rosa (5 moedas)": a quantidade antes, na ordem de leitura (o TikTok põe o "x5" depois porque é
    // o contador do combo subindo na tela; escolha do usuário em 2026-10-23)
    private static object[] Presente(PresenteDoTikTok presente)
    {
        var partes = new List<object> { "enviou " };
        if (presente.Quantidade > 1)
            partes.AddRange([new { value = presente.Quantidade.ToString(CultureInfo.InvariantCulture) + "x" }, " "]);
        if (presente.Imagem != null)
            partes.Add(new { image = presente.Imagem, name = presente.Nome });
        partes.Add(new { value = presente.Nome });
        if (presente.Moedas > 0)
            partes.Add(presente.Moedas == 1 ? " (1 moeda)" : $" ({presente.Moedas.ToString("N0", new CultureInfo("pt-BR"))} moedas)");
        return [.. partes];
    }

    /// <summary>
    /// Chat Multiplataforma: como está a leitura do YouTube, da Kick ou do TikTok (que o app faz), para o aviso de conexão da página
    /// juntar todas as plataformas.
    /// </summary>
    public static string EstadoDaPlataforma(string plataforma, string estado) =>
        $"window.oncChat && window.oncChat.platformState && window.oncChat.platformState({JsonSerializer.Serialize(plataforma)}, " +
        $"{JsonSerializer.Serialize(estado)});";

    /// <summary>O estado do leitor do YouTube como a página entende.</summary>
    public static string EstadoParaAPagina(EstadoDoYouTube estado) => estado switch
    {
        EstadoDoYouTube.Procurando => "connecting",
        EstadoDoYouTube.Conectado => "connected",
        EstadoDoYouTube.EsperandoALive => "waiting",
        EstadoDoYouTube.CanalNaoExiste => "notfound",
        EstadoDoYouTube.SemConexao => "failed",
        _ => "off",
    };

    /// <summary>O estado do leitor do TikTok como a página entende.</summary>
    public static string EstadoParaAPagina(EstadoDoTikTok estado) => estado switch
    {
        EstadoDoTikTok.Procurando => "connecting",
        EstadoDoTikTok.Conectado => "connected",
        EstadoDoTikTok.EsperandoALive => "waiting",
        EstadoDoTikTok.CanalNaoExiste => "notfound",
        EstadoDoTikTok.SemConexao => "failed",
        _ => "off",
    };

    /// <summary>O estado do leitor da Kick como a página entende.</summary>
    public static string EstadoParaAPagina(EstadoDaKick estado) => estado switch
    {
        EstadoDaKick.Conectando => "connecting",
        EstadoDaKick.Conectado => "connected",
        EstadoDaKick.CanalNaoExiste => "notfound",
        EstadoDaKick.SemConexao => "failed",
        _ => "off",
    };

    /// <summary>
    /// Uma mensagem do chat da Kick (Chat Multiplataforma): o texto e os emotes como <c>{ emote: { src }, name }</c>, os
    /// papéis que valem para os filtros (dono, moderador, VIP) e os selos na ordem do site: <c>"moderator/1"</c> (um da
    /// Twitch, que a página já tem) ou <c>{ src, name }</c> (uma imagem da Kick).
    /// </summary>
    public static string AdicionarDaKick(ItemDaKick item)
    {
        string json = JsonSerializer.Serialize(new
        {
            id = item.Id,
            login = item.Login,
            name = item.Nome,
            color = item.Cor,
            roles = item.Papeis,
            badges = item.Selos.Select(s => s.DaTwitch != null ? (object)s.DaTwitch : new { src = s.Imagem, name = s.Nome }),
            parts = item.Partes.Select(p => p.Imagem == null ? (object)p.Texto : new { emote = new { src = p.Imagem }, name = p.Texto }),
        });
        return $"window.oncChat && window.oncChat.addKick && window.oncChat.addKick({json});";
    }

    /// <summary>
    /// Um moderador da Kick apagou uma mensagem (<c>{ id }</c>), baniu ou suspendeu alguém (<c>{ login }</c>: as mensagens
    /// dele saem) ou limpou o chat (<c>{ all: true }</c>). Null para uma mensagem nova.
    /// </summary>
    public static string? ApagarDaKick(EventoDaKick evento)
    {
        object? oQue = evento.Tipo switch
        {
            TipoDeEventoDaKick.Apagada => new { id = evento.Id },
            TipoDeEventoDaKick.Banido => new { login = evento.Login },
            TipoDeEventoDaKick.Limpeza => new { all = true },
            _ => null,
        };
        return oQue == null ? null : $"window.oncChat && window.oncChat.removeKick && window.oncChat.removeKick({JsonSerializer.Serialize(oQue)});";
    }

    /// <summary>Um moderador do TikTok apagou mensagens: pelos ids delas e pelo @ de quem mandou.</summary>
    public static string ApagarDoTikTok(IReadOnlyList<string> ids, IReadOnlyList<string> logins) =>
        $"window.oncChat && window.oncChat.removeTikTok && window.oncChat.removeTikTok({JsonSerializer.Serialize(ids)}, " +
        $"{JsonSerializer.Serialize(logins)});";

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

    /// <summary>
    /// "Mostrar viewers" sem as bordas (com elas, a faixa do canal mostra): a linha no topo do Padrão, com o logo e o
    /// número curto de cada plataforma e a soma. null, ou sem nenhum número, tira a linha.
    /// </summary>
    public static string MostrarViewers(Viewers? viewers)
    {
        var itens = new List<object>();
        if (viewers != null)
        {
            foreach ((string plataforma, long? numero) in new[]
                     { ("twitch", viewers.Twitch), ("youtube", viewers.YouTube), ("kick", viewers.Kick), ("tiktok", viewers.TikTok) })
            {
                if (numero is { } n)
                    itens.Add(new { platform = plataforma, text = Viewers.Curto(n) });
            }
        }
        string dados = itens.Count == 0
            ? "null"
            : JsonSerializer.Serialize(new { items = itens, total = viewers?.Total is { } total ? Viewers.Curto(total) : null });
        return $"window.oncChat && window.oncChat.viewers && window.oncChat.viewers({dados});";
    }

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
            "onc:pause-state:paused" => MensagemDaPagina.ChatPausado,
            "onc:pause-state:reading" => MensagemDaPagina.ChatContinuando,
            "onc:pause-state:live" => MensagemDaPagina.ChatAoVivo,
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
