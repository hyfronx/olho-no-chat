#nullable enable
namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Onde a janela do chat é redimensionada (com as bordas visíveis): as quatro bordas (decisão 18) e, no canto de baixo à
/// direita, um quadrado inteiro de 16 px, não só os poucos pixels onde as bordas se encontram. Os números são os que o
/// Windows espera na resposta ao WM_NCHITTEST.
/// </summary>
public static class BordasDaJanela
{
    public const int Nenhuma = 0;
    public const int Esquerda = 10;
    public const int Direita = 11;
    public const int Cima = 12;
    public const int CimaEsquerda = 13;
    public const int CimaDireita = 14;
    public const int Baixo = 15;
    public const int BaixoEsquerda = 16;
    public const int BaixoDireita = 17;

    /// <summary>A largura das bordas da esquerda, da direita e de baixo (a margem da página).</summary>
    public const double Borda = LogicaJanelaChat.MargemDasBordas;

    /// <summary>A borda de cima fica sobre a barra laranja: mais fina, para não atrapalhar os botões.</summary>
    public const double BordaDeCima = 4;

    /// <summary>O lado do quadrado de baixo à direita, e até onde as outras quinas chegam.</summary>
    public const double Canto = 16;

    /// <summary>Qual parte da moldura está no ponto (em unidades da janela), ou <see cref="Nenhuma"/>.</summary>
    public static int Parte(double x, double y, double largura, double altura)
    {
        if (x < 0 || y < 0 || x >= largura || y >= altura)
            return Nenhuma;

        if (x >= largura - Canto && y >= altura - Canto)
            return BaixoDireita;
        if (y < BordaDeCima)
            return x < Canto ? CimaEsquerda : x >= largura - Canto ? CimaDireita : Cima;
        if (y >= altura - Borda)
            return x < Canto ? BaixoEsquerda : Baixo;
        if (x < Borda)
            return y < Canto ? CimaEsquerda : y >= altura - Canto ? BaixoEsquerda : Esquerda;
        if (x >= largura - Borda)
            return y < Canto ? CimaDireita : Direita;
        return Nenhuma;
    }
}
