namespace OlhoNoChat.Chat;

/// <summary>Os avisos que as páginas do chat mandam ao app (<see cref="ContratoComAPagina.LerMensagem"/>).</summary>
public enum MensagemDaPagina
{
    /// <summary>Uma mensagem de outro programa ou que o app não conhece: ignorada.</summary>
    Desconhecida,

    /// <summary>Padrão: apareceu uma mensagem que pode tocar o som (o app decide se toca).</summary>
    TocarSom,

    /// <summary>Padrão: conectando ao chat da Twitch.</summary>
    Conectando,

    /// <summary>Padrão: conectado ao chat da Twitch.</summary>
    Conectado,

    /// <summary>Padrão: sem conexão (a página tenta de novo sozinha).</summary>
    Desconectado,

    /// <summary>Padrão: clicaram no aviso do modo rolagem.</summary>
    SairDoModoRolagem,

    /// <summary>Chat oficial: a mensagem digitada na caixa da Twitch foi enviada.</summary>
    EscritaEnviada,

    /// <summary>Chat oficial: Esc na caixa da Twitch.</summary>
    EscritaCancelada,

    /// <summary>Padrão, Chat Multiplataforma: procurando o canal da Kick ou conectando ao chat dele.</summary>
    KickConectando,

    /// <summary>Padrão, Chat Multiplataforma: lendo o chat da Kick.</summary>
    KickConectado,

    /// <summary>Padrão, Chat Multiplataforma: sem conexão com a Kick (a página tenta de novo sozinha).</summary>
    KickDesconectado,

    /// <summary>Padrão, Chat Multiplataforma: a Kick disse que o canal não existe (não tenta de novo).</summary>
    KickNaoExiste,

    /// <summary>Padrão: sem canal da Kick para ler.</summary>
    KickDesligado,
}
