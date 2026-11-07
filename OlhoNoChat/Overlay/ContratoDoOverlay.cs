using System.Text.Json;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Overlay;

/// <summary>
/// O que vai e vem do overlay da live: as opções que a página do overlay (browser/overlay.js) lê e o que a página do chat
/// Padrão (browser/chat.js) manda para o overlay (<c>onc:overlay:{...}</c>), também as linhas do botão de teste.
/// </summary>
public static class ContratoDoOverlay
{
    /// <summary>O começo das mensagens da página do chat para o overlay; depois vem o JSON.</summary>
    public const string Prefixo = "onc:overlay:";

    // Uma linha com muitos emotes e selos passa de 10 mil caracteres; mais que isso não é uma linha do chat
    public const int TamanhoMaximoDoHtml = 200_000;
    public const int TamanhoMaximoDoSeletor = 2_000;

    /// <summary>As fontes da lista da aba Overlay (as do Windows: o OBS roda no mesmo PC).</summary>
    public static readonly IReadOnlyList<string> Fontes = ["Segoe UI", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Bahnschrift"];

    /// <summary>As animações de entrar e sair que a página do overlay conhece (browser/overlay.css), e "none".</summary>
    public static readonly IReadOnlyList<string> Animacoes = ["slide", "fade", "rise", "pop", "bounce", "blur", "flip", "none"];

    /// <summary>O que a página do chat pediu para o overlay.</summary>
    public enum TipoDoPedido
    {
        Linha,
        Remover,
        /// <summary>Uma linha de exemplo do botão "Mensagens de teste" (não espera o atraso).</summary>
        Teste,
        /// <summary>Uma linha já mandada mudou (os selos chegaram depois): o seletor dela e o HTML novo.</summary>
        Substituir,
        Limpar,
    }

    /// <summary>Um pedido lido: a linha (HTML) ou o seletor das que saem ou mudam (e o HTML novo); vazio no Limpar.</summary>
    public sealed record Pedido(TipoDoPedido Tipo, string Valor, string Html = "");

    /// <summary>As opções do overlay, no JSON que a página dele lê.</summary>
    public static string JsonDasOpcoes(Opcoes opcoes) => JsonSerializer.Serialize(new
    {
        fadeSeconds = opcoes.OverlaySome ? opcoes.SegundosParaSumirNoOverlay : 0,
        delaySeconds = opcoes.SegundosDeAtrasoNoOverlay,
        maxLines = opcoes.MensagensNoOverlay,
        // Quantas couberem na fonte (o maxLines não vale)
        fill = opcoes.PreencherAreaNoOverlay,
        fontSize = opcoes.TamanhoDoTextoNoOverlay,
        font = opcoes.FonteDoOverlay,
        bold = opcoes.NegritoNoOverlay,
        outline = opcoes.ContornoNoOverlay,
        background = opcoes.FundoNoOverlay,
        backgroundOpacity = Math.Round(opcoes.OpacidadeDoFundoNoOverlay / 100.0, 2),
        direction = opcoes.DirecaoDoOverlay,
        align = opcoes.AlinhamentoDoOverlay,
        // "Perspectiva": "none", "left" ou "right" (o lado mais para dentro da tela) e a inclinação em graus
        softEdge = opcoes.SaidaSuaveNoOverlay,
        perspective = opcoes.PerspectivaNoOverlay,
        perspectiveShape = opcoes.FormatoDaPerspectivaNoOverlay,
        cone = opcoes.ConeDoFunilNoOverlay,
        tilt = opcoes.InclinacaoNoOverlay,
        animation = opcoes.AnimacaoDoOverlay,
        // "Esconder GIFs" do chat (aba Chat) vale também para a live
        hideGifs = opcoes.EsconderGifs,
        badges = opcoes.SelosNoOverlay,
        time = opcoes.HorarioNoOverlay,
        // O ícone só faz sentido com mais de uma plataforma
        platform = opcoes.PlataformaNoOverlay && opcoes.ChatMultiplataforma,
        firstTime = opcoes.PrimeiraVezNoOverlay,
        layout = opcoes.DisposicaoDoOverlay,
        tickerSpeed = opcoes.VelocidadeDaFaixaNoOverlay,
        // O chat marca as linhas de comando (data-command) pelos "Prefixos dos comandos" da aba Chat
        hideCommands = opcoes.EsconderComandosNoOverlay,
        hideBots = opcoes.EsconderBotsNoOverlay,
        bannedWords = opcoes.PalavrasProibidasNoOverlay.Select(p => p.Trim()).Where(p => p.Length > 0),
        links = opcoes.LinksNoOverlay,
        maxChars = opcoes.LimitarTamanhoNoOverlay ? opcoes.CaracteresNoOverlay : 0,
        maxEmotes = opcoes.LimitarEmotesNoOverlay ? opcoes.EmotesNoOverlay : 0,
        notices = new
        {
            subs = opcoes.InscricoesNoOverlay,
            raids = opcoes.RaidsNoOverlay,
            twitchOther = opcoes.OutrosAvisosDaTwitchNoOverlay,
            redemptions = opcoes.ResgatesNoOverlay,
            superchats = opcoes.SuperChatsNoOverlay,
            youtubeMembers = opcoes.MembrosDoYouTubeNoOverlay,
            tiktokGifts = opcoes.PresentesDoTikTokNoOverlay,
            tiktokOther = opcoes.OutrosAvisosDoTikTokNoOverlay,
        },
    });

    /// <summary>
    /// Uma mensagem da página do chat para o overlay (<c>onc:overlay:{"t":"line","html":...}</c>,
    /// <c>{"t":"remove","selector":...}</c>, <c>{"t":"replace","selector":...,"html":...}</c> ou <c>{"t":"clear"}</c>); null se
    /// não for uma, ou se vier estragada.
    /// </summary>
    public static Pedido? LerDaPagina(string? mensagem)
    {
        if (mensagem == null || !mensagem.StartsWith(Prefixo, StringComparison.Ordinal))
            return null;
        try
        {
            using JsonDocument documento = JsonDocument.Parse(mensagem.AsMemory(Prefixo.Length));
            JsonElement raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("t", out JsonElement tipo))
                return null;
            return tipo.GetString() switch
            {
                "line" when Texto(raiz, "html", TamanhoMaximoDoHtml) is { } html => new Pedido(TipoDoPedido.Linha, html),
                "test" when Texto(raiz, "html", TamanhoMaximoDoHtml) is { } html => new Pedido(TipoDoPedido.Teste, html),
                "remove" when Texto(raiz, "selector", TamanhoMaximoDoSeletor) is { } seletor => new Pedido(TipoDoPedido.Remover, seletor),
                "replace" when Texto(raiz, "selector", TamanhoMaximoDoSeletor) is { } seletor
                               && Texto(raiz, "html", TamanhoMaximoDoHtml) is { } html => new Pedido(TipoDoPedido.Substituir, seletor, html),
                "clear" => new Pedido(TipoDoPedido.Limpar, string.Empty),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement raiz, string nome, int maximo) =>
        raiz.TryGetProperty(nome, out JsonElement valor) && valor.ValueKind == JsonValueKind.String
        && valor.GetString() is { Length: > 0 } texto && texto.Length <= maximo
            ? texto
            : null;
}
