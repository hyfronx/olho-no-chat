#nullable enable
namespace OlhoNoChat.Chat;

/// <summary>
/// A página local de boas-vindas (browser/index.html), quando falta o canal ou o endereço. Com "?canal" ela manda
/// digitar o canal na faixa logo acima; sem, manda abrir as Configurações pela engrenagem. Não recebe CSS nem script.
/// </summary>
public sealed class PaginaDeBoasVindas : PaginaDoChat
{
    public PaginaDeBoasVindas(bool comCanal)
        : base(EnderecoDaPagina(comCanal), "boas-vindas|" + EnderecoDaPagina(comCanal))
    {
    }

    private static string EnderecoDaPagina(bool comCanal)
    {
        string pagina = new Uri(InfoDoApp.PaginaDeBoasVindas).AbsoluteUri;
        return comCanal ? pagina + "?canal" : pagina;
    }
}
