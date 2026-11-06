using CommunityToolkit.Mvvm.ComponentModel;

namespace OlhoNoChat.Janelas.NovaVersao;

/// <summary>O que a janela "Nova versão disponível" mostra e a caixa "Não procurar atualizações automaticamente".</summary>
public sealed partial class LogicaNovaVersao : ObservableObject
{
    /// <param name="voltaParaANormal">Saindo da beta ("Receber versões beta" desligado): a versão normal pode ter um
    /// número menor que o da beta instalada, então a janela diz que volta, e não que é nova.</param>
    public LogicaNovaVersao(string versaoAtual, string versaoNova, bool voltaParaANormal = false)
    {
        VersaoAtual = versaoAtual;
        VersaoNova = versaoNova;
        VoltaParaANormal = voltaParaANormal;
    }

    public bool VoltaParaANormal { get; }

    public string Titulo => VoltaParaANormal ? "Voltar para a versão normal do Olho no Chat?" : "Tem uma versão nova do Olho no Chat!";

    public string RotuloDaNova => VoltaParaANormal ? "Versão normal:" : "Versão nova:";

    public string TextoDoBotao => VoltaParaANormal ? "Voltar agora" : "Atualizar agora";

    public string VersaoAtual { get; }

    public string VersaoNova { get; }

    /// <summary>Vale só para "Depois", Esc e "×" (em "Atualizar agora" é ignorada).</summary>
    [ObservableProperty]
    private bool _naoProcurarAutomaticamente;
}
