#nullable enable
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>Aba "Som" de Configurações (DataContext = <see cref="LogicaSom"/>).</summary>
public partial class PaginaSom : UserControl
{
    public PaginaSom()
    {
        InitializeComponent();
    }

    private LogicaSom? Logica => DataContext as LogicaSom;

    private void ListaSom_DropDownClosed(object? sender, EventArgs e) => Logica?.Ouvir();

    private void BotaoEscolherPasta_Click(object sender, RoutedEventArgs e)
    {
        var escolha = new OpenFolderDialog();
        if (escolha.ShowDialog(Window.GetWindow(this)) == true)
            Logica?.MudarPasta(escolha.FolderName);
    }
}
