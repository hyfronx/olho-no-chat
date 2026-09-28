using System.Text.Json.Nodes;

namespace OlhoNoChat.Testes.Configuracoes;

/// <summary>Monta os arquivos no formato das versões até a 1.5 (Jot: lista de itens {"Type", "Name", "Value"}).</summary>
internal static class ArquivoAntigoFalso
{
    /// <summary>
    /// As 54 chaves do AppSettings.json da 1.3.4 (COMPORTAMENTO.md, "Todas as chaves de GeneralSettings"), todas com
    /// um valor diferente do padrão.
    /// </summary>
    public static JsonObject TodasAsChavesDiferentesDoPadrao() => new()
    {
        ["Username"] = "canalteste",
        ["FadeChat"] = true,
        ["FadeTime"] = "45",
        ["BlockBotActivity"] = false,
        ["HideGifs"] = true,
        ["HideOtherChannels"] = true,
        ["ChatNotificationSound"] = "coins.wav",
        ["ChatSoundQuietSeconds"] = 30,
        ["ThemeIndex"] = 0,
        ["CustomCSS"] = "body { color: red; }",
        ["TwitchPopoutCSS"] = ".chat-line__message { color: blue; }",
        ["UseDefaultTwitchPopoutCSS"] = false,
        ["ChatType"] = 1,
        ["CustomURL"] = "https://exemplo.invalid/chat",
        ["ZoomLevel"] = 1.35,
        ["OpacityLevel"] = 90,
        ["AutoHideBorders"] = true,
        ["HideTaskbarIcon"] = true,
        ["AllowInteraction"] = false,
        ["HighlightUsersChat"] = true,
        ["AllowedUsersOnlyChat"] = true,
        ["FilterAllowAllMods"] = true,
        ["FilterAllowAllVIPs"] = true,
        ["AllowedUsersList"] = new JsonArray("Amigo1", "amiga2"),
        ["BlockedUsersList"] = new JsonArray("chato"),
        ["RedemptionsEnabled"] = true,
        ["ChannelID"] = "121292674",
        ["OAuthToken"] = "tokenfalso123",
        ["TwitchLogin"] = "hyfronx",
        ["TwitchDisplayName"] = "Hyfronx",
        ["UseTwitchChatBox"] = true,
        ["CloseChatBoxAfterSend"] = true,
        ["BetterTtv"] = false,
        ["BetterTtv_7tv"] = false,
        ["BetterTtv_AdvEmoteMenu"] = false,
        ["FrankerFaceZ"] = false,
        ["CheckForUpdates"] = false,
        ["ChatMessageColor"] = "#FFF3A6",
        ["ChatTextOutline"] = "soft",
        ["ChatFontFamily"] = "Verdana",
        ["ShowMessageTime"] = true,
        ["ChatHighlightColor"] = "#80112233",
        ["ChatHighlightModsColor"] = "#90445566",
        ["ChatHighlightVIPsColor"] = "#A0778899",
        ["OutputVolume"] = 0.35,
        ["DeviceName"] = "Fones (Audeze Maxwell)",
        ["DeviceID"] = 2,
        ["SoundClipsFolder"] = @"C:\Sons",
        ["ToggleBordersHotkey"] = Atalho(99, 6),
        ["ToggleInteractableHotkey"] = Atalho(44, 1),
        ["AlwaysOnTop"] = false,
        ["BringToTopHotkey"] = null,
        ["WriteMessageHotkey"] = Atalho(100, 2),
        ["AllowMultipleInstances"] = true,
    };

    public static JsonObject Atalho(int tecla, int modificadores) => new() { ["Key"] = tecla, ["Modifiers"] = modificadores };

    /// <summary>O AppSettings.json com o valor dado, como o Jot gravava.</summary>
    public static string Opcoes(JsonObject valor) => new JsonArray(new JsonObject
    {
        ["Type"] = "OlhoNoChat.GeneralSettings, OlhoNoChat, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        ["Name"] = "GeneralSettings",
        ["Value"] = valor,
    }).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>O MainWindow_State.json, como o Jot gravava.</summary>
    public static string Janela(double esquerda, double topo, double largura, double altura, int estado)
    {
        JsonObject Item(string tipo, string nome, JsonNode valor) => new() { ["Type"] = tipo, ["Name"] = nome, ["Value"] = valor };
        const string real = "System.Double, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
        return new JsonArray(
            Item(real, "Top", topo),
            Item(real, "Width", largura),
            Item(real, "Height", altura),
            Item(real, "Left", esquerda),
            Item("System.Windows.WindowState, PresentationFramework, Version=10.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                 "WindowState", estado)).ToJsonString();
    }

    /// <summary>Um arquivo mínimo da 1.0.17 ou mais nova (tem o atalho de escrever) com as chaves dadas.</summary>
    public static JsonObject Minimo(params (string Chave, JsonNode? Valor)[] chaves)
    {
        var valor = new JsonObject { ["WriteMessageHotkey"] = Atalho(100, 3) };
        foreach (var (chave, v) in chaves)
            valor[chave] = v;
        return valor;
    }
}
