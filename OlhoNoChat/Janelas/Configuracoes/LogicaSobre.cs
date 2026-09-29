using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>A aba Sobre: a versão e os links (não tem opções, não entra na conferência de mudanças).</summary>
public sealed partial class LogicaSobre
{
    public const string EnderecoDasConexoes = "https://www.twitch.tv/settings/connections";

    /// <summary>A versão instalada, ou "de desenvolvimento".</summary>
    public string Versao => InfoDoApp.Versao;

    [RelayCommand]
    private static void AbrirConexoes() => AbrirNoWindows.Site(EnderecoDasConexoes);

    [RelayCommand]
    private static void AbrirProjeto() => AbrirNoWindows.Site(InfoDoApp.EnderecoDoRepositorio);
}
