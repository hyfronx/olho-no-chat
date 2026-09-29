using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>Um item de uma lista de cores: uma cor pronta ou "Personalizada…".</summary>
public sealed partial class ItemDeCor : ObservableObject
{
    public ItemDeCor(string nome, Color? cor)
    {
        Nome = nome;
        Cor = cor;
        _amostra = cor;
    }

    public string Nome { get; }

    /// <summary>A cor pronta; null no item "Personalizada…".</summary>
    public Color? Cor { get; }

    public bool EhPersonalizada => Cor == null;

    /// <summary>A amostra antes do nome. Na "Personalizada…" é a cor digitada, ou nada (só o contorno) enquanto ela não vale.</summary>
    [ObservableProperty]
    private Color? _amostra;

    public override string ToString() => Nome;
}
