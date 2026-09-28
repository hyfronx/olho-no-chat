#nullable enable
using System.Windows.Media;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Um item de uma lista das abas: o nome mostrado e o valor que fica gravado.</summary>
/// <param name="Amostra">A cor mostrada antes do nome (lista de cores); null = só o contorno.</param>
/// <param name="Fonte">A fonte em que o nome aparece (lista de fontes).</param>
public sealed record OpcaoDaLista(string Nome, string Valor, Color? Amostra = null, string Fonte = "Segoe UI")
{
    // A lista mostra o nome (também para a automação e os leitores de tela)
    public override string ToString() => Nome;
}
