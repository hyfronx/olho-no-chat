#nullable enable
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Chat;

/// <summary>
/// Qualquer página de chat (StreamElements, Streamlabs...): o endereço exatamente como foi digitado, sem validação, e o
/// CSS personalizado. Sem script, som, filtros nem modo rolagem; tudo o que muda recarrega.
/// </summary>
public sealed class ChatEnderecoPersonalizado : PaginaDoChat
{
    public ChatEnderecoPersonalizado(Opcoes opcoes)
        : base(opcoes.EnderecoPersonalizado, string.Join("|", "endereco", opcoes.EnderecoPersonalizado, opcoes.CssDoEnderecoPersonalizado))
    {
    }

    public override string Css(Opcoes opcoes) => opcoes.CssDoEnderecoPersonalizado;
}
