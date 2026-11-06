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

#if DEBUG
    /// <summary>
    /// Os pacotes do teste de atualização local (ferramentas\teste_atualizacao_local.ps1): o app de teste instalado
    /// procura versões novas só aqui, nunca no GitHub.
    /// </summary>
    public static string PastaDoTesteDeAtualizacao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OncTesteDeAtualizacao-Pacotes");
#endif

    // App "Olho no Chat" registrado em dev.twitch.tv. Não pode mudar: um acesso dado a outro Client ID é esquecido
    // na verificação, e todo mundo teria que conectar de novo.
    public const string TwitchClientId = "zrqsilh31pbdlfjb81onhulkvzytgh";

    // O que "Conectar" pede: escrever no chat, ler os resgates de pontos do canal, listar os emotes da conta e aprovar ou
    // recusar as mensagens que o AutoMod segurou (só vale nos canais em que a conta é moderadora)
    public const string PermissoesDaTwitch = "user:write:chat channel:read:redemptions user:read:emotes moderator:manage:automod";

    /// <summary>Configurações e dados do navegador interno (%AppData%\OlhoNoChat).</summary>
    public static string PastaDeDados =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), NomeDaPastaDeDados);

    /// <summary>
    /// A sessão da conta do TikTok (<see cref="TikTok.CofreDoTikTok"/>): em %LocalAppData%, só neste computador (não vai
    /// junto quando o Windows sincroniza o perfil), numa pasta fechada só para o usuário, fora da pasta da instalação.
    /// </summary>
    public static string PastaDoCofre =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NomeDaPastaDeDados + "-Cofre");

    /// <summary>O nome da chave do cofre no chip de segurança (TPM).</summary>
    public const string ChaveDoCofre = NomeDaPastaDeDados + ".SessaoDoTikTok";

    /// <summary>
    /// Esta é uma versão beta (o número tem hífen, como 2.10.0-beta.1): as janelas ficam roxas e com o selo BETA. No app
    /// de teste (Debug), a variável de ambiente ONC_BETA=1 mostra o visual da beta.
    /// </summary>
    public static bool EhBeta => _ehBeta.Value;

    private static readonly Lazy<bool> _ehBeta = new(() =>
#if DEBUG
        Environment.GetEnvironmentVariable("ONC_BETA") == "1" ||
#endif
        (VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion?.IsPrerelease == true));

    /// <summary>O selo BETA das janelas: visível só na beta.</summary>
    public static System.Windows.Visibility VisibilidadeDaBeta =>
        EhBeta ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Versão instalada pelo Velopack, ou "de desenvolvimento" rodando direto da compilação.</summary>
    public static string Versao => VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString() ?? "de desenvolvimento";

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
