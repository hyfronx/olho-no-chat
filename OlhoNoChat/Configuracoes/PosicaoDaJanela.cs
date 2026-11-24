using System.Windows;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Configuracoes;

/// <summary>
/// Onde a janela do chat estava e o tamanho dela, gravados quando ela fecha. Os números são os do WPF: pixels divididos
/// pela escala do Windows do monitor onde a janela estava, que vai junto em <paramref name="Escala"/> (1,25 = 125%;
/// 0 = gravada por uma versão que ainda não guardava a escala).
/// </summary>
public sealed record PosicaoDaJanela(double Esquerda, double Topo, double Largura, double Altura, WindowState Estado, double Escala = 0)
{
    /// <summary>Antes de a janela aparecer. O estado vem por último, para maximizar no monitor da posição gravada.</summary>
    public void AplicarEm(Window janela)
    {
        janela.Top = Topo;
        janela.Width = Largura;
        janela.Height = Altura;
        janela.Left = Esquerda;
        janela.WindowState = Estado;
    }

    /// <summary>
    /// O lugar gravado em pixels de verdade, ou null sem a escala. Ao abrir, o WPF cria a janela com a escala do monitor
    /// principal: num monitor com outra escala ela nasceria deslocada (e mais a cada abertura), então a janela do chat
    /// é recolocada por este retângulo.
    /// </summary>
    public Retangulo? EmPixels() => Escala > 0
        ? Retangulo.De((int)Math.Round(Esquerda * Escala), (int)Math.Round(Topo * Escala),
                       (int)Math.Round(Largura * Escala), (int)Math.Round(Altura * Escala))
        : null;
}
