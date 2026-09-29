using CommunityToolkit.Mvvm.ComponentModel;

namespace OlhoNoChat.Janelas.NovaVersao;

/// <summary>O que a janela "Nova versão disponível" mostra e a caixa "Não procurar atualizações automaticamente".</summary>
public sealed partial class LogicaNovaVersao : ObservableObject
{
    public LogicaNovaVersao(string versaoAtual, string versaoNova)
    {
        VersaoAtual = versaoAtual;
        VersaoNova = versaoNova;
    }

    public string VersaoAtual { get; }

    public string VersaoNova { get; }

    /// <summary>Vale só para "Depois", Esc e "×" (em "Atualizar agora" é ignorada).</summary>
    [ObservableProperty]
    private bool _naoProcurarAutomaticamente;
}
