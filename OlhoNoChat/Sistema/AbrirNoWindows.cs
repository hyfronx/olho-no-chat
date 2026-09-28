#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Abre um site no navegador padrão da pessoa e uma pasta no Explorer. Quando não dá, explica numa caixa de
/// mensagem (o app nunca abre página de site dentro dele).
/// </summary>
internal static class AbrirNoWindows
{
    public static void Site(string endereco)
    {
        if (string.IsNullOrWhiteSpace(endereco) || !Uri.IsWellFormedUriString(endereco, UriKind.Absolute))
        {
            Avisar($"O endereço não é válido: {endereco}", "Endereço inválido", MessageBoxImage.Error);
            return;
        }

        Abrir(endereco,
            erroDoSistema: erro => ($"Não foi possível abrir o site. Erro do sistema: {erro}", "Não deu certo"),
            outroErro: _ => ("Ocorreu um erro inesperado ao abrir o site. Confira se há um navegador padrão configurado no Windows.", "Erro"));
    }

    public static void Pasta(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            Avisar("O caminho da pasta está vazio.", "Caminho inválido", MessageBoxImage.Error);
            return;
        }
        if (!Directory.Exists(caminho))
        {
            Avisar($"A pasta não existe:\n\n{caminho}", "Pasta não encontrada", MessageBoxImage.Warning);
            return;
        }

        Abrir(caminho,
            erroDoSistema: erro => ($"Não foi possível abrir a pasta. Erro do sistema: {erro}", "Não deu certo"),
            outroErro: erro => ($"Ocorreu um erro inesperado ao abrir a pasta:\n\n{erro}", "Erro"));
    }

    // O Windows escolhe o programa: o navegador padrão para um endereço, o Explorer para uma pasta
    private static void Abrir(string alvo, Func<string, (string Texto, string Titulo)> erroDoSistema,
        Func<string, (string Texto, string Titulo)> outroErro)
    {
        try
        {
            Process.Start(new ProcessStartInfo(alvo) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            var (texto, titulo) = erroDoSistema(ex.Message);
            Avisar(texto, titulo, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Não abriu '{alvo}': {ex}");
            var (texto, titulo) = outroErro(ex.Message);
            Avisar(texto, titulo, MessageBoxImage.Error);
        }
    }

    private static void Avisar(string texto, string titulo, MessageBoxImage icone) =>
        MessageBox.Show(texto, titulo, MessageBoxButton.OK, icone);
}
