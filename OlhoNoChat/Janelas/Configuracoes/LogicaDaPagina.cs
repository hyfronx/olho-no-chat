#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// O que toda aba de Configurações com opções faz: mostrar as opções salvas, gravar as da tela (só no "Salvar") e
/// dizer o que está na tela, para a janela saber se algo mudou. As abas Chat, Aparência e Som mostram só as opções do
/// tipo de chat escolhido na lista (<see cref="TipoNaTela"/>), antes de salvar.
/// </summary>
public abstract class LogicaDaPagina : ObservableObject
{
    private ChatTypes _tipoNaTela;

    /// <summary>O tipo de chat escolhido na aba Chat (ainda sem salvar).</summary>
    public ChatTypes TipoNaTela
    {
        get => _tipoNaTela;
        set
        {
            if (!SetProperty(ref _tipoNaTela, value))
                return;
            OnPropertyChanged(nameof(ComPadrao));
            OnPropertyChanged(nameof(ComChatOficial));
            OnPropertyChanged(nameof(ComEndereco));
            OnPropertyChanged(nameof(ComTextoDasMensagens));
            TipoMudou();
        }
    }

    public bool ComPadrao => TipoNaTela == ChatTypes.Padrao;
    public bool ComChatOficial => TipoNaTela == ChatTypes.TwitchPopout;
    public bool ComEndereco => TipoNaTela == ChatTypes.CustomURL;

    /// <summary>"Texto das mensagens" vale para o Padrão e para o chat oficial (que imita o Padrão).</summary>
    public bool ComTextoDasMensagens => ComPadrao || ComChatOficial;

    /// <summary>Mostra os valores salvos (ao abrir a janela e depois de "Restaurar tudo para o padrão").</summary>
    public abstract void Carregar(Opcoes opcoes);

    /// <summary>Passa o que está na tela para as opções, seguindo as regras do <see cref="TipoNaTela"/>.</summary>
    public abstract void Gravar(Opcoes opcoes);

    /// <summary>
    /// Os valores na tela, com um nome cada, inclusive os escondidos pelo tipo de chat. Mudar e voltar ao valor de
    /// antes dá o mesmo estado.
    /// </summary>
    public abstract void Estado(IDictionary<string, string> estado);

    protected virtual void TipoMudou()
    {
    }
}
