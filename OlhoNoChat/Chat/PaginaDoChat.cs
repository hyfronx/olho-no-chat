using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Chat;

/// <summary>
/// Uma página que a janela mostra: o endereço, o que ela só lê ao abrir (<see cref="ChaveDeRecarga"/>), o CSS e os
/// scripts que o app põe nela quando termina de carregar, e o que vale ao vivo ao salvar as Configurações.
/// </summary>
public abstract class PaginaDoChat
{
    protected PaginaDoChat(string endereco, string chaveDeRecarga)
    {
        Endereco = endereco;
        ChaveDeRecarga = chaveDeRecarga;
    }

    public string Endereco { get; }

    /// <summary>
    /// Tudo o que a página aberta usou e só lê ao abrir. Ao salvar, se a página das opções novas tem outra chave, o chat
    /// recarrega (e perde as mensagens da tela); senão, o resto é aplicado ao vivo.
    /// </summary>
    public string ChaveDeRecarga { get; }

    /// <summary>O canal do chat (Padrão e chat oficial); null nas outras páginas.</summary>
    public virtual string? Canal => null;

    /// <summary>Scripts que rodam antes do CSS, um de cada vez (as extensões de emotes do chat oficial).</summary>
    public virtual IReadOnlyList<string> ScriptsAntesDoCss(Opcoes opcoes) => [];

    /// <summary>O CSS do app para esta página ("" = nenhum).</summary>
    public virtual string Css(Opcoes opcoes) => string.Empty;

    /// <summary>O script da página, que roda logo depois do CSS ("" = nenhum).</summary>
    public virtual string Script(Opcoes opcoes) => string.Empty;

    /// <summary>
    /// O CSS e o script num pedido só, para as primeiras linhas já aparecerem com o visual certo; null se não há nada.
    /// </summary>
    public string? ScriptAoCarregar(Opcoes opcoes)
    {
        string css = Css(opcoes);
        string script = Script(opcoes);
        if (css.Length == 0 && script.Length == 0)
            return null;
        return css.Length == 0 ? script : ContratoComAPagina.PorOCss(css) + "\n" + script;
    }

    /// <summary>O que "Salvar" muda na página aberta sem recarregar; null = nada.</summary>
    public virtual string? ScriptAoVivo(Opcoes opcoes) => null;

    /// <summary>
    /// A página precisa responder <c>true</c> ao <see cref="ScriptAoVivo"/>; se ainda está carregando, se o script dela
    /// não roda ou se falha, o chat recarrega.
    /// </summary>
    public virtual bool RecarregaSeNaoAplicar => false;

    /// <summary>
    /// A página das opções: o chat do canal salvo, o endereço personalizado, ou as boas-vindas quando falta o canal ou o
    /// endereço. No Chat Multiplataforma a Twitch é opcional: com o canal do YouTube ou o da Kick, o Padrão abre sem ela.
    /// </summary>
    public static PaginaDoChat DasOpcoes(Opcoes opcoes)
    {
        // O Chat Multiplataforma só existe no Padrão
        TipoDeChat tipo = opcoes.ChatMultiplataforma ? TipoDeChat.Padrao : TiposDeChat.Ler(opcoes.TipoDeChat);
        string canal = CanalSalvo(opcoes);

        if (TiposDeChat.UsaCanal(tipo))
        {
            if (canal.Length == 0 && !ChatMultiplataforma.TemOutroCanal(opcoes))
                return new PaginaDeBoasVindas(comCanal: true);
            return tipo == TipoDeChat.ChatOficial ? new ChatOficialDaTwitch(canal, opcoes) : new ChatPadrao(canal, opcoes);
        }

        return string.IsNullOrWhiteSpace(opcoes.EnderecoPersonalizado)
            ? new PaginaDeBoasVindas(comCanal: false)
            : new ChatEnderecoPersonalizado(opcoes);
    }

    /// <summary>O canal salvo como nome (versões antigas podiam gravar um link).</summary>
    public static string CanalSalvo(Opcoes opcoes)
    {
        string salvo = opcoes.Canal ?? string.Empty;
        string nome = NomesDaTwitch.Extrair(salvo);
        return nome.Length > 0 ? nome : salvo.Trim();
    }
}
