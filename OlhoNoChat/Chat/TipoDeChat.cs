namespace OlhoNoChat.Chat;

/// <summary>
/// O que a janela mostra (aba Chat das Configurações). Os números são os gravados no arquivo (<c>TipoDeChat</c>): não
/// podem mudar.
/// </summary>
public enum TipoDeChat
{
    /// <summary>"Padrão (recomendado)": a página própria do app (browser/chat.html).</summary>
    Padrao = 0,

    /// <summary>"Chat oficial da Twitch": a página popout da Twitch, com o CSS e os scripts do app.</summary>
    ChatOficial = 1,

    /// <summary>"Endereço personalizado": qualquer página, com o CSS digitado pela pessoa.</summary>
    EnderecoPersonalizado = 2,
}
