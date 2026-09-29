using System.Windows;

namespace OlhoNoChat.Configuracoes;

/// <summary>Onde a janela do chat estava e o tamanho dela, gravados quando ela fecha.</summary>
public sealed record PosicaoDaJanela(double Esquerda, double Topo, double Largura, double Altura, WindowState Estado)
{
    public static PosicaoDaJanela De(Window janela) =>
        new(janela.Left, janela.Top, janela.Width, janela.Height, janela.WindowState);

    /// <summary>Antes de a janela aparecer. O estado vem por último, para maximizar no monitor da posição gravada.</summary>
    public void AplicarEm(Window janela)
    {
        janela.Top = Topo;
        janela.Width = Largura;
        janela.Height = Altura;
        janela.Left = Esquerda;
        janela.WindowState = Estado;
    }
}
