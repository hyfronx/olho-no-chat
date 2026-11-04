using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Onde a janela do chat abre: na primeira vez, no centro do monitor principal (decisão 18); depois, onde estava, a não
/// ser que esse lugar não esteja em nenhum monitor (um monitor desligado ou trocado): aí volta para o centro do principal
/// (decisão 7).
/// </summary>
public static class PosicaoNaTela
{
    /// <summary>O pedaço da janela que precisa estar num monitor para dar para pegá-la (a barra laranja).</summary>
    public const int LarguraMinimaVisivel = 50;
    public const int AlturaMinimaVisivel = 30;

    /// <summary>Algum pedaço de pelo menos 50 × 30 px da janela está na área útil de um dos monitores.</summary>
    public static bool EstaVisivel(Retangulo janela, IEnumerable<Retangulo> areasDosMonitores)
    {
        foreach (Retangulo area in areasDosMonitores)
        {
            int largura = Math.Min(janela.Direita, area.Direita) - Math.Max(janela.Esquerda, area.Esquerda);
            int altura = Math.Min(janela.Baixo, area.Baixo) - Math.Max(janela.Topo, area.Topo);
            if (largura >= LarguraMinimaVisivel && altura >= AlturaMinimaVisivel)
                return true;
        }
        return false;
    }

    /// <summary>A janela no centro da área, do mesmo tamanho (ou do tamanho da área, se não couber).</summary>
    public static Retangulo NoCentro(Retangulo janela, Retangulo area)
    {
        int largura = Math.Min(janela.Largura, area.Largura);
        int altura = Math.Min(janela.Altura, area.Altura);
        return Retangulo.De(area.Esquerda + (area.Largura - largura) / 2, area.Topo + (area.Altura - altura) / 2, largura, altura);
    }

    /// <summary>
    /// Onde a janela deve ficar ao abrir, ou null para deixar onde está. <paramref name="primeiraVez"/> = nenhuma posição
    /// gravada.
    /// </summary>
    public static Retangulo? AoAbrir(Retangulo janela, bool primeiraVez, IReadOnlyList<Retangulo> areasDosMonitores, Retangulo areaDoPrincipal)
    {
        if (primeiraVez || !EstaVisivel(janela, areasDosMonitores))
            return NoCentro(janela, areaDoPrincipal);
        return null;
    }

    /// <summary>
    /// Uma janela gravada menor que o tamanho mínimo de agora (a largura mínima subiu na 2.6.0 e a altura mínima chegou)
    /// cresce para a direita e para baixo. Se por isso ela passar da borda direita ou de baixo da área do monitor, volta
    /// para dentro só o que cresceu: nunca mais que isso, nem para fora da borda esquerda ou de cima. Null = fica onde está.
    /// </summary>
    /// <param name="cresceuX">Quantos pixels a janela ganhou na largura pelo mínimo.</param>
    /// <param name="cresceuY">Quantos pixels a janela ganhou na altura pelo mínimo.</param>
    public static Retangulo? PuxarParaDentro(Retangulo janela, Retangulo area, int cresceuX, int cresceuY)
    {
        int dx = Math.Max(0, Math.Min(Math.Min(janela.Direita - area.Direita, cresceuX), janela.Esquerda - area.Esquerda));
        int dy = Math.Max(0, Math.Min(Math.Min(janela.Baixo - area.Baixo, cresceuY), janela.Topo - area.Topo));
        return dx == 0 && dy == 0 ? null : Retangulo.De(janela.Esquerda - dx, janela.Topo - dy, janela.Largura, janela.Altura);
    }
}
