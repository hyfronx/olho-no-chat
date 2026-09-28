#nullable enable
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>
/// Uma das três cores dos filtros (destaque, moderadores, VIPs): a lista das cores prontas mais "Personalizada…",
/// que mostra uma caixa para digitar "#RRGGBB". Uma cor digitada que não vale nunca é salva: fica a cor salva antes.
/// </summary>
public sealed partial class EscolhaDeCor : ObservableObject
{
    private Color _salva;
    private bool _erro;

    public EscolhaDeCor(Color salva)
    {
        _salva = salva;
        Itens = [.. CoresDeDestaque.Prontas.Select(p => new ItemDeCor(p.Nome, p.Cor)), new ItemDeCor(CoresDeDestaque.NomeDaPersonalizada, null)];
        Personalizada = Itens[^1];

        // Uma cor que não está entre as prontas (inclusive com outra transparência) aparece como "Personalizada…"
        ItemDeCor? pronta = Itens.FirstOrDefault(i => i.Cor == salva);
        _escolhida = pronta ?? Personalizada;
        if (pronta == null)
            _textoPersonalizado = CoresDeDestaque.ParaTexto(salva);
        Personalizada.Amostra = CorPersonalizada;
    }

    public IReadOnlyList<ItemDeCor> Itens { get; }

    public ItemDeCor Personalizada { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarCaixa), nameof(MostrarErro))]
    private ItemDeCor _escolhida;

    /// <summary>O que está digitado na caixa da "Personalizada…".</summary>
    [ObservableProperty]
    private string _textoPersonalizado = string.Empty;

    /// <summary>A lista aparece na tela (a página esconde algumas conforme o modo da lista).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarCaixa), nameof(MostrarErro))]
    private bool _visivel = true;

    /// <summary>A caixa só aparece com "Personalizada…" escolhida e a lista na tela.</summary>
    public bool MostrarCaixa => Visivel && Escolhida.EhPersonalizada;

    /// <summary>"Use o formato #RRGGBB." embaixo da caixa.</summary>
    public bool MostrarErro => _erro && MostrarCaixa;

    public Color? CorPersonalizada => CoresDeDestaque.Ler(TextoPersonalizado, _salva);

    /// <summary>A cor escolhida; a digitada que não vale conta como a salva.</summary>
    public Color Cor => Escolhida.Cor ?? CorPersonalizada ?? _salva;

    partial void OnTextoPersonalizadoChanged(string value)
    {
        Personalizada.Amostra = CorPersonalizada;
        if (CorPersonalizada != null)
            MudarErro(false); // o erro some assim que a cor fica válida
    }

    /// <summary>Saiu da caixa: com algo digitado que não é uma cor, mostra o erro.</summary>
    public void SaiuDaCaixa()
    {
        if (!string.IsNullOrWhiteSpace(TextoPersonalizado))
            MudarErro(CorPersonalizada == null);
    }

    /// <summary>A cor para gravar ("Salvar"). Mostra o erro se a digitada não vale (também com a caixa vazia).</summary>
    public Color Salvar()
    {
        MudarErro(MostrarCaixa && CorPersonalizada == null);
        _salva = Cor;
        return _salva;
    }

    private void MudarErro(bool erro)
    {
        _erro = erro;
        OnPropertyChanged(nameof(MostrarErro));
    }
}
