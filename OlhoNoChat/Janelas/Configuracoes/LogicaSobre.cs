using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>A aba Sobre: a versão e os links (não tem opções, não entra na conferência de mudanças).</summary>
public sealed partial class LogicaSobre
{
    public const string EnderecoDasConexoes = "https://www.twitch.tv/settings/connections";

    // Os sons que vêm com o app pedem crédito (CC BY 4.0); a lista de cada som fica no CREDITOS.txt
    public const string EnderecoDosSons = "https://notificationsounds.com";
    public const string EnderecoDaLicencaDosSons = "https://creativecommons.org/licenses/by/4.0/deed.pt-br";

    /// <summary>A versão instalada, ou "de desenvolvimento".</summary>
    public string Versao => InfoDoApp.Versao;

    [RelayCommand]
    private static void AbrirConexoes() => AbrirNoWindows.Site(EnderecoDasConexoes);

    [RelayCommand]
    private static void AbrirProjeto() => AbrirNoWindows.Site(InfoDoApp.EnderecoDoRepositorio);

    [RelayCommand]
    private static void AbrirSiteDosSons() => AbrirNoWindows.Site(EnderecoDosSons);

    [RelayCommand]
    private static void AbrirLicencaDosSons() => AbrirNoWindows.Site(EnderecoDaLicencaDosSons);
}
