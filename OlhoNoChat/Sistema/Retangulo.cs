#nullable enable
namespace OlhoNoChat.Sistema;

/// <summary>Um retângulo da tela, em pixels.</summary>
public readonly record struct Retangulo(int Esquerda, int Topo, int Direita, int Baixo)
{
    public int Largura => Direita - Esquerda;
    public int Altura => Baixo - Topo;

    public static Retangulo De(int esquerda, int topo, int largura, int altura) => new(esquerda, topo, esquerda + largura, topo + altura);
}
