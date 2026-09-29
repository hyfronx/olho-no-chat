using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Chat;

/// <summary>
/// O chat Padrão: a página própria do app (browser/chat.html) servida em https://olhonochat.example. Ela só conecta
/// quando recebe as opções (<c>oncChat.start</c>), e quase tudo muda ao vivo (<c>oncChat.apply</c> e o CSS).
/// </summary>
public sealed class ChatPadrao : PaginaDoChat
{
    private readonly string _canal;

    public ChatPadrao(string canal, Opcoes opcoes)
        : base(InfoDoApp.EnderecoDoChatPadrao(canal, temaPadrao: opcoes.Tema != Opcoes.TemaNenhum),
               $"padrao|{canal}|{opcoes.Tema}")
    {
        _canal = canal;
    }

    public override string? Canal => _canal;

    public override string Css(Opcoes opcoes) => CssDoChat.DoChatPadrao(opcoes);

    public override string Script(Opcoes opcoes) => ContratoComAPagina.Iniciar(opcoes);

    // Filtros, destaques, som, "Apagar mensagens antigas", GIFs, outros canais e o texto das mensagens
    public override string ScriptAoVivo(Opcoes opcoes) => ContratoComAPagina.AplicarAoVivo(opcoes, Css(opcoes));

    public override bool RecarregaSeNaoAplicar => true;
}
