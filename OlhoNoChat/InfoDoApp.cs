#nullable enable
using System.IO;
using Velopack.Locators;

namespace OlhoNoChat;

/// <summary>
/// Dados fixos do app: pastas, versão, endereços e o app registrado na Twitch.
/// </summary>
public static class InfoDoApp
{
#if DEBUG
    // A versão de desenvolvimento tem pasta de dados própria (e canal de instância única próprio), para ser
    // testada com o app instalado aberto ao lado
    public const string NomeDaPastaDeDados = "OlhoNoChat-Dev";
#else
    public const string NomeDaPastaDeDados = "OlhoNoChat";
#endif

    public const string EnderecoDoRepositorio = "https://github.com/hyfronx/olho-no-chat";

    // App "Olho no Chat" registrado em dev.twitch.tv. Não pode mudar: um acesso dado a outro Client ID é esquecido
    // na verificação, e todo mundo teria que conectar de novo.
    public const string TwitchClientId = "zrqsilh31pbdlfjb81onhulkvzytgh";

    // O que "Conectar" pede: escrever no chat, ler os resgates de pontos do canal e listar os emotes da conta
    public const string PermissoesDaTwitch = "user:write:chat channel:read:redemptions user:read:emotes";

    /// <summary>Configurações e dados do navegador interno (%AppData%\OlhoNoChat).</summary>
    public static string PastaDeDados =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), NomeDaPastaDeDados);

    /// <summary>Versão instalada pelo Velopack, ou "de desenvolvimento" rodando direto da compilação.</summary>
    public static string Versao => VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString() ?? "de desenvolvimento";

    /// <summary>Rodando do zip portátil do Velopack.</summary>
    public static bool EhPortatil => VelopackLocator.Current?.IsPortable ?? false;

    /// <summary>A pasta "browser" ao lado do .exe (página do chat Padrão e página de boas-vindas).</summary>
    public static string PastaDasPaginas => Path.Combine(AppContext.BaseDirectory, "browser");

    /// <summary>
    /// Nome com que o navegador interno serve a pasta "browser": a página do chat Padrão ganha um endereço https de
    /// verdade e pode carregar emotes de outros sites. ".example" é reservado, nunca é um site real.
    /// </summary>
    public const string HostDasPaginas = "olhonochat.example";

    public static string PaginaDeBoasVindas => Path.Combine(PastaDasPaginas, "index.html");

    /// <summary>Endereço do chat Padrão de um canal; <paramref name="temaPadrao"/> = tema "Padrão".</summary>
    public static string EnderecoDoChatPadrao(string canal, bool temaPadrao)
    {
        string endereco = $"https://{HostDasPaginas}/chat.html?canal={Uri.EscapeDataString(canal)}";
        return temaPadrao ? endereco + "&tema=padrao" : endereco;
    }
}
