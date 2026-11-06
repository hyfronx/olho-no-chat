using System.Windows.Media;

namespace OlhoNoChat.Estilos;

/// <summary>
/// As cores de destaque do app, lidas por Estilos/Cores.xaml quando ele é carregado (x:Static): o laranja na versão
/// normal e o roxo numa versão beta (<see cref="InfoDoApp.EhBeta"/>), para a beta nunca ser confundida com a normal. A
/// escolha acontece uma vez, antes da primeira janela, e vale para todas (moldura, barra do chat, botão principal,
/// interruptores, títulos, cartões de aviso).
/// </summary>
public static class Paleta
{
    private static bool Beta => InfoDoApp.EhBeta;

    // Laranja do app: moldura, interruptores ligados, caixa com o foco, item escolhido, botão principal
    public static Color CorLaranja => Beta ? Color.FromRgb(0x7C, 0x4D, 0xFF) : Color.FromRgb(0xE8, 0x5D, 0x30);

    // Laranja claro: títulos de seção, ícones dos cartões, avisos de erro, links
    public static Color CorLaranjaClaro => Beta ? Color.FromRgb(0xB3, 0x9D, 0xFF) : Color.FromRgb(0xFF, 0x8A, 0x65);

    public static Color CorLaranjaComMouse => Beta ? Color.FromRgb(0x8C, 0x63, 0xFF) : Color.FromRgb(0xF0, 0x6A, 0x3E);

    public static Color CorLaranjaApertado => Beta ? Color.FromRgb(0x6A, 0x3D, 0xE8) : Color.FromRgb(0xD4, 0x53, 0x2A);

    public static Color CorLaranjaTransparente => Color.FromArgb(0x4D, CorLaranja.R, CorLaranja.G, CorLaranja.B);

    public static Color CorLaranjaTransparenteComMouse => Color.FromArgb(0x66, CorLaranja.R, CorLaranja.G, CorLaranja.B);

    // Os cartões de aviso: um marrom alaranjado na normal, um roxo escuro na beta
    public static Color CorFundoDoCartaoDeAviso => Beta ? Color.FromRgb(0x22, 0x1D, 0x2A) : Color.FromRgb(0x2A, 0x21, 0x1D);

    public static Color CorBordaDoCartaoDeAviso => Beta ? Color.FromRgb(0x3A, 0x2E, 0x52) : Color.FromRgb(0x4A, 0x32, 0x26);

    public static Color CorTextoDoCartaoDeAviso => Beta ? Color.FromRgb(0xDC, 0xD3, 0xEA) : Color.FromRgb(0xE6, 0xD6, 0xCF);
}
