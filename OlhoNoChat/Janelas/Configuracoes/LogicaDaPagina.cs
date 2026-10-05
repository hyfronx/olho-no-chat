using CommunityToolkit.Mvvm.ComponentModel;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// O que toda aba de Configurações com opções faz: mostrar as opções salvas, gravar as da tela (só no "Salvar") e
/// dizer o que está na tela, para a janela saber se algo mudou. As abas Chat, Aparência e Som mostram só as opções do
/// tipo de chat escolhido na lista (<see cref="TipoNaTela"/>), antes de salvar.
/// </summary>
public abstract class LogicaDaPagina : ObservableObject
{
    private TipoDeChat _tipoNaTela;

    /// <summary>O tipo de chat escolhido na aba Chat (ainda sem salvar).</summary>
    public TipoDeChat TipoNaTela
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

    private bool _multiplataformaNaTela;

    /// <summary>O Chat Multiplataforma ligado na aba Chat (ainda sem salvar).</summary>
    public bool MultiplataformaNaTela
    {
        get => _multiplataformaNaTela;
        set
        {
            if (SetProperty(ref _multiplataformaNaTela, value))
                OnPropertyChanged(nameof(SemMultiplataforma));
        }
    }

    /// <summary>Os recursos só da Twitch podem ser usados (desligados no Chat Multiplataforma).</summary>
    public bool SemMultiplataforma => !MultiplataformaNaTela;

    public bool ComPadrao => TipoNaTela == TipoDeChat.Padrao;
    public bool ComChatOficial => TipoNaTela == TipoDeChat.ChatOficial;
    public bool ComEndereco => TipoNaTela == TipoDeChat.EnderecoPersonalizado;

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
