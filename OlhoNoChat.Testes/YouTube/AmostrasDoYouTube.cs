namespace OlhoNoChat.Testes.YouTube;

/// <summary>
/// Pedaços de páginas e respostas do YouTube no formato das de verdade, só com os campos que interessam. Os valores
/// entram no lugar das marcas %…%.
/// </summary>
internal static class AmostrasDoYouTube
{
    public const string Versao = "2.20261002.10.00";

    /// <summary>A página /live de um canal ao vivo: o link do vídeo, a configuração do site e o chat (com outra continuação antes).</summary>
    public static string PaginaComLive(string continuacao = "cont0", string extra = "") => """
        <!DOCTYPE html><html lang="pt-BR"><head>
        <link rel="canonical" href="https://www.youtube.com/watch?v=jfKfPfyJRdk">
        <script>ytcfg.set({"INNERTUBE_API_KEY":"chave1","INNERTUBE_CONTEXT_CLIENT_VERSION":"2.20261002.10.00","INNERTUBE_CONTEXT":{"client":{"hl":"pt","gl":"BR"}}});</script>
        </head><body><script>var ytInitialData = {"contents":{"twoColumnWatchNextResults":{"results":{"continuations":[{"nextContinuationData":{"continuation":"outra"}}]},
        "conversationBar":{"liveChatRenderer":{"continuations":[{"reloadContinuationData":{"continuation":"%CONT%","clickTrackingParams":"CH4Q"}}],
        "header":{}}}}},"videoDetails":{"videoId":"jfKfPfyJRdk","isLiveContent":true%EXTRA%}};</script></body></html>
        """.Replace("%CONT%", continuacao).Replace("%EXTRA%", extra);

    /// <summary>Sem live, o /live do canal mostra a página do próprio canal.</summary>
    public const string PaginaSemLive = """
        <!DOCTYPE html><html><head><link rel="canonical" href="https://www.youtube.com/channel/UC_xvULzXXNAcidoUs4KqinQ">
        <script>ytcfg.set({"INNERTUBE_API_KEY":"chave1","INNERTUBE_CONTEXT_CLIENT_VERSION":"2.20261002.10.00"});</script>
        </head><body><script>var ytInitialData = {"contents":{"items":[{"continuation":"daLista","canonicalBaseUrl":"/watch?v=abcdefghijk"}]}};</script></body></html>
        """;

    /// <summary>Uma resposta do get_live_chat com estas ações e a próxima continuação (null: a live acabou).</summary>
    public static string Resposta(string? continuacao, params string[] acoes) => """
        {"responseContext":{"serviceTrackingParams":[]},"continuationContents":{"liveChatContinuation":{%CONTINUACOES%
        "actions":[%ACOES%],"trackingParams":"CAEQ"}}}
        """
        .Replace("%CONTINUACOES%", continuacao == null ? string.Empty : """
            "continuations":[{"invalidationContinuationData":{"invalidationId":{"objectSource":1056,"topic":"chat~jfKfPfyJRdk"},
            "timeoutMs":10000,"continuation":"%CONT%"}}],
            """.Replace("%CONT%", continuacao))
        .Replace("%ACOES%", string.Join(",", acoes));

    /// <summary>Uma mensagem de texto (addChatItemAction com um liveChatTextMessageRenderer).</summary>
    public static string Texto(string id, string texto = "oi chat", DateTimeOffset? enviadaEm = null, string selos = "") =>
        Acao("liveChatTextMessageRenderer", """
            "message":{"runs":[{"text":"%TEXTO%"}]},"authorName":{"simpleText":"@Viewer1"},
            "authorPhoto":{"thumbnails":[{"url":"https://yt4.ggpht.com/foto=s32","width":32,"height":32}]},
            "id":"%ID%","timestampUsec":"%USEC%","authorExternalChannelId":"UCRkBGtMuQ0QCzW9-wB6ldEQ"%SELOS%
            """.Replace("%TEXTO%", texto).Replace("%ID%", id).Replace("%USEC%", Usec(enviadaEm).ToString()).Replace("%SELOS%", selos));

    /// <summary>Uma ação addChatItemAction com o renderer e os campos dele.</summary>
    public static string Acao(string renderer, string campos) => """
        {"clickTrackingParams":"CAEQl98B","addChatItemAction":{"item":{"%RENDERER%":{%CAMPOS%}},"clientId":"CPa6sYfLpZcD"}}
        """.Replace("%RENDERER%", renderer).Replace("%CAMPOS%", campos);

    public static long Usec(DateTimeOffset? quando = null) => (quando ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() * 1000;

    public const string SeloDeMembro = """
        ,"authorBadges":[{"liveChatAuthorBadgeRenderer":{"customThumbnail":{"thumbnails":[{"url":"https://yt3.ggpht.com/selo=s16"}]},
        "tooltip":"Membro (6 meses)","accessibility":{"accessibilityData":{"label":"Membro (6 meses)"}}}}]
        """;

    public const string SeloDeModerador = """
        ,"authorBadges":[{"liveChatAuthorBadgeRenderer":{"icon":{"iconType":"MODERATOR"},"tooltip":"Moderador",
        "accessibility":{"accessibilityData":{"label":"Moderador"}}}}]
        """;

    public const string SeloDeDono = """
        ,"authorBadges":[{"liveChatAuthorBadgeRenderer":{"icon":{"iconType":"OWNER"},"tooltip":"Proprietário"}}]
        """;
}
