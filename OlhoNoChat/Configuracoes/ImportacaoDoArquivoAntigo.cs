using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Chat;
using OlhoNoChat.Som;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Configuracoes;

/// <summary>
/// Lê os arquivos das versões até a 1.5 (AppSettings.json com as opções e MainWindow_State.json com a posição da
/// janela) e aplica as migrações que elas faziam ao abrir. Só lê: quem move os arquivos é o
/// <see cref="ArquivoDeConfiguracoes"/>, depois de o arquivo novo estar gravado.
/// </summary>
/// <remarks>
/// Os dois arquivos são listas de itens {"Type", "Name", "Value"}. O item é achado pelo nome e o "Type" é ignorado
/// (muda com o nome do app e a versão do .NET). Chave que falta ou com valor que não dá para ler fica com o padrão;
/// chave desconhecida é ignorada, como o app antigo fazia.
/// </remarks>
public static class ImportacaoDoArquivoAntigo
{
    public const string ArquivoDasOpcoes = "AppSettings.json";
    public const string ArquivoDaJanela = "MainWindow_State.json";

    /// <param name="Opcoes">As opções convertidas (já com <see cref="Opcoes.Corrigir"/>).</param>
    /// <param name="AntesDa1017">
    /// Arquivo de antes da 1.0.17, de um app muito diferente: começa como na primeira instalação e só o canal fica.
    /// </param>
    /// <param name="OpcoesIlegiveis">AppSettings.json existe mas não é um JSON que dê para ler: ficaram os padrões.</param>
    public sealed record Resultado(Opcoes Opcoes, bool AntesDa1017, bool OpcoesIlegiveis);

    private static readonly JsonDocumentOptions Leitura = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Os arquivos antigos da pasta, ou null quando não há nenhum dos dois.</summary>
    public static Resultado? Ler(string pasta)
    {
        string? opcoes = LerSeExistir(Path.Combine(pasta, ArquivoDasOpcoes));
        string? janela = LerSeExistir(Path.Combine(pasta, ArquivoDaJanela));
        return opcoes == null && janela == null ? null : Converter(opcoes, janela);
    }

    private static string? LerSeExistir(string caminho) => File.Exists(caminho) ? File.ReadAllText(caminho) : null;

    /// <summary>O texto dos dois arquivos (null = o arquivo não existe) nas opções novas.</summary>
    public static Resultado Converter(string? textoDasOpcoes, string? textoDaJanela)
    {
        var opcoes = new Opcoes();
        bool ilegivel = false;
        JsonObject? valor = null;

        if (textoDasOpcoes != null)
        {
            try
            {
                if (JsonNode.Parse(textoDasOpcoes, documentOptions: Leitura) is JsonArray itens)
                    valor = AcharValor(itens, "GeneralSettings") as JsonObject
                            // Nome de item diferente: o que tem o canal (como a verificação de versões antigas fazia)
                            ?? itens.OfType<JsonObject>().Select(i => i["Value"] as JsonObject)
                                    .FirstOrDefault(v => v != null && v.ContainsKey("Username"));
                else
                    ilegivel = true;
            }
            catch (JsonException)
            {
                ilegivel = true;
            }
        }

        // Toda versão desde a 1.0.17 grava o atalho de escrever: sem ele, o arquivo é de uma versão muito antiga
        if (valor != null && valor.ContainsKey("Username") && !valor.ContainsKey("WriteMessageHotkey"))
        {
            string canal = NomesDaTwitch.Extrair(Texto(valor["Username"]) ?? string.Empty);
            opcoes.Canal = NomesDaTwitch.EhValido(canal) ? canal.ToLowerInvariant() : string.Empty;
            opcoes.Corrigir();
            return new Resultado(opcoes, AntesDa1017: true, OpcoesIlegiveis: false);
        }

        if (valor != null)
            AplicarOpcoes(valor, opcoes);
        if (textoDaJanela != null)
            opcoes.Janela = LerJanela(textoDaJanela);

        opcoes.Corrigir();
        return new Resultado(opcoes, AntesDa1017: false, OpcoesIlegiveis: ilegivel);
    }

    private static JsonNode? AcharValor(JsonArray itens, string nome) =>
        itens.OfType<JsonObject>().FirstOrDefault(i => Texto(i["Name"]) == nome)?["Value"];

    private static void AplicarOpcoes(JsonObject v, Opcoes o)
    {
        // Chat
        o.Canal = Texto(v["Username"]) ?? o.Canal;
        o.TipoDeChat = Inteiro(v["ChatType"]) ?? o.TipoDeChat;
        o.EnderecoPersonalizado = Texto(v["CustomURL"]) ?? o.EnderecoPersonalizado;
        o.ApagarMensagensAntigas = Booleano(v["FadeChat"]) ?? o.ApagarMensagensAntigas;
        o.SegundosParaApagar = Texto(v["FadeTime"]) ?? o.SegundosParaApagar;
        o.EsconderBots = Booleano(v["BlockBotActivity"]) ?? o.EsconderBots;
        o.EsconderGifs = Booleano(v["HideGifs"]) ?? o.EsconderGifs;
        o.EsconderOutrosCanais = Booleano(v["HideOtherChannels"]) ?? o.EsconderOutrosCanais;
        o.BetterTtv = Booleano(v["BetterTtv"]) ?? o.BetterTtv;
        o.Emotes7tv = Booleano(v["BetterTtv_7tv"]) ?? o.Emotes7tv;
        o.MenuDeEmotesDoBetterTtv = Booleano(v["BetterTtv_AdvEmoteMenu"]) ?? o.MenuDeEmotesDoBetterTtv;
        o.FrankerFaceZ = Booleano(v["FrankerFaceZ"]) ?? o.FrankerFaceZ;

        // Aparência
        o.Tema = Inteiro(v["ThemeIndex"]) ?? o.Tema;
        o.AparenciaPadraoNoChatOficial = Booleano(v["UseDefaultTwitchPopoutCSS"]) ?? o.AparenciaPadraoNoChatOficial;
        o.CssDoChatOficial = Texto(v["TwitchPopoutCSS"]) ?? o.CssDoChatOficial;
        o.CorDoTexto = Texto(v["ChatMessageColor"]) ?? o.CorDoTexto;
        o.ContornoDasLetras = Texto(v["ChatTextOutline"]) ?? o.ContornoDasLetras;
        o.Fonte = Texto(v["ChatFontFamily"]) ?? o.Fonte;
        o.MostrarHorario = Booleano(v["ShowMessageTime"]) ?? o.MostrarHorario;
        SepararOCss(v, o);

        // Barra laranja
        o.TamanhoDoTexto = Real(v["ZoomLevel"]) ?? o.TamanhoDoTexto;
        o.Fundo = Inteiro(v["OpacityLevel"]) is int fundo && fundo is >= 0 and <= 255 ? (byte)fundo : o.Fundo;
        o.SempreNoTopo = Booleano(v["AlwaysOnTop"]) ?? o.SempreNoTopo;

        // Som
        o.SomDeMensagem = Texto(v["ChatNotificationSound"]) ?? o.SomDeMensagem;
        o.SegundosEntreSons = Inteiro(v["ChatSoundQuietSeconds"]) ?? o.SegundosEntreSons;
        o.Volume = Real(v["OutputVolume"]) is double volume ? (float)volume : o.Volume;
        o.SaidaDeSom = Inteiro(v["DeviceID"]) ?? o.SaidaDeSom;
        o.NomeDaSaidaDeSom = Texto(v["DeviceName"]) ?? o.NomeDaSaidaDeSom;
        o.PastaDosSons = Texto(v["SoundClipsFolder"]) ?? o.PastaDosSons;
        // Os sons "Alert …" que vinham com o app foram trocados por outros depois da 1.2.0: um deles salvo vira o
        // padrão novo (só na pasta do app; uma pasta escolhida pela pessoa pode ter as próprias cópias)
        if (o.PastaDosSons == SonsDisponiveis.PastaPadrao && o.SomDeMensagem.StartsWith("Alert ", StringComparison.Ordinal))
            o.SomDeMensagem = Opcoes.SomPadrao;

        // Geral
        o.EsconderBordasAoAbrir = Booleano(v["AutoHideBorders"]) ?? o.EsconderBordasAoAbrir;
        o.EsconderIconeDaBarraDeTarefas = Booleano(v["HideTaskbarIcon"]) ?? o.EsconderIconeDaBarraDeTarefas;
        // "AllowInteraction" ("Permitir clicar no chat com as bordas visíveis") saiu: com as bordas o chat sempre aceita clique
        o.ProcurarAtualizacoes = Booleano(v["CheckForUpdates"]) ?? o.ProcurarAtualizacoes;
        o.PermitirVariasCopias = Booleano(v["AllowMultipleInstances"]) ?? o.PermitirVariasCopias;
        o.AtalhoBordas = AtalhoAntigo(v, "ToggleBordersHotkey", o.AtalhoBordas);
        o.AtalhoModoRolagem = AtalhoAntigo(v, "ToggleInteractableHotkey", o.AtalhoModoRolagem);
        o.AtalhoSempreNoTopo = AtalhoAntigo(v, "BringToTopHotkey", o.AtalhoSempreNoTopo);
        o.AtalhoEscrever = AtalhoAntigo(v, "WriteMessageHotkey", o.AtalhoEscrever);

        // Twitch
        o.MostrarResgates = Booleano(v["RedemptionsEnabled"]) ?? o.MostrarResgates;
        o.CaixaDaTwitch = Booleano(v["UseTwitchChatBox"]) ?? o.CaixaDaTwitch;
        o.FecharCaixaDepoisDeEnviar = Booleano(v["CloseChatBoxAfterSend"]) ?? o.FecharCaixaDepoisDeEnviar;
        o.Conta.Id = Texto(v["ChannelID"]) ?? o.Conta.Id;
        o.Conta.Token = Texto(v["OAuthToken"]) ?? o.Conta.Token;
        o.Conta.Login = Texto(v["TwitchLogin"]) ?? o.Conta.Login;
        o.Conta.NomeDeExibicao = Texto(v["TwitchDisplayName"]) ?? o.Conta.NomeDeExibicao;

        // Filtros do chat
        o.DestacarUsuarios = Booleano(v["HighlightUsersChat"]) ?? o.DestacarUsuarios;
        o.SoUsuariosDaLista = Booleano(v["AllowedUsersOnlyChat"]) ?? o.SoUsuariosDaLista;
        o.DestacarModeradores = Booleano(v["FilterAllowAllMods"]) ?? o.DestacarModeradores;
        o.DestacarVips = Booleano(v["FilterAllowAllVIPs"]) ?? o.DestacarVips;
        o.ListaDeUsuarios = Lista(v["AllowedUsersList"]) ?? o.ListaDeUsuarios;
        o.UsuariosBloqueados = Lista(v["BlockedUsersList"]) ?? o.UsuariosBloqueados;
        o.CorDoDestaque = ConversorDeCor.Ler(Texto(v["ChatHighlightColor"])) ?? o.CorDoDestaque;
        o.CorDosModeradores = ConversorDeCor.Ler(Texto(v["ChatHighlightModsColor"])) ?? o.CorDosModeradores;
        o.CorDosVips = ConversorDeCor.Ler(Texto(v["ChatHighlightVIPsColor"])) ?? o.CorDosVips;
    }

    // Uma chave só (CustomCSS) guardava o CSS do tema "Nenhum" e o do "Endereço personalizado". Com o tipo salvo dá
    // para saber de qual era; nos outros casos não se sabe, e o CSS vai para os dois.
    private static void SepararOCss(JsonObject v, Opcoes o)
    {
        string? css = Texto(v["CustomCSS"]);
        if (css == null)
            return;

        int? tipo = Inteiro(v["ChatType"]);
        int? tema = Inteiro(v["ThemeIndex"]);
        if (tipo == (int)TipoDeChat.EnderecoPersonalizado)
            o.CssDoEnderecoPersonalizado = css;
        else if (tipo == (int)TipoDeChat.Padrao && tema == Opcoes.TemaNenhum)
            o.CssDoTemaNenhum = css;
        else
            o.CssDoTemaNenhum = o.CssDoEnderecoPersonalizado = css;
    }

    private static PosicaoDaJanela? LerJanela(string texto)
    {
        try
        {
            if (JsonNode.Parse(texto, documentOptions: Leitura) is not JsonArray itens)
                return null;
            double? esquerda = Real(AcharValor(itens, "Left"));
            double? topo = Real(AcharValor(itens, "Top"));
            double? largura = Real(AcharValor(itens, "Width"));
            double? altura = Real(AcharValor(itens, "Height"));
            int estado = Inteiro(AcharValor(itens, "WindowState")) is int e && Enum.IsDefined(typeof(WindowState), e) ? e : 0;
            return esquerda is double x && topo is double y && largura is double l && altura is double a
                ? new PosicaoDaJanela(x, y, l, a, (WindowState)estado)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // {"Key": n, "Modifiers": m}; null = a pessoa apagou o atalho (fica sem); outro valor = o padrão
    private static Atalho? AtalhoAntigo(JsonObject v, string chave, Atalho? padrao)
    {
        if (!v.TryGetPropertyValue(chave, out JsonNode? no))
            return padrao;
        if (no == null)
            return null;
        if (no is JsonObject atalho && Inteiro(atalho["Key"]) is int tecla && Enum.IsDefined(typeof(Key), tecla))
            return new Atalho((Key)tecla, (ModifierKeys)(Inteiro(atalho["Modifiers"]) ?? 0));
        return padrao;
    }

    // Leitura tolerante como a do app antigo (um número onde era texto vira o texto do número, "true" vira true...)

    private static string? Texto(JsonNode? no) => no is JsonValue valor
        ? valor.GetValueKind() switch
        {
            JsonValueKind.String => valor.GetValue<string>(),
            JsonValueKind.Number => valor.ToJsonString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => null,
        }
        : null;

    private static bool? Booleano(JsonNode? no) => no is JsonValue valor
        ? valor.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(valor.GetValue<string>(), out bool b) ? b : null,
            JsonValueKind.Number => valor.GetValue<double>() != 0,
            _ => null,
        }
        : null;

    private static double? Real(JsonNode? no) => no is JsonValue valor
        ? valor.GetValueKind() switch
        {
            JsonValueKind.Number => valor.GetValue<double>(),
            JsonValueKind.String => double.TryParse(valor.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null,
            _ => null,
        }
        : null;

    private static int? Inteiro(JsonNode? no) =>
        Real(no) is double d && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;

    private static List<string>? Lista(JsonNode? no) => no switch
    {
        JsonArray itens => itens.Select(Texto).OfType<string>().ToList(),
        _ => null,
    };
}
