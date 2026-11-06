using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    // Um clique num som da lista aberta: escolhe, toca e deixa a lista aberta para ouvir o próximo (pedido do usuário,
    // 2026-11-06). Clicar no som que já está escolhido fecha a lista, como sempre (o clique segue para o ComboBox)
    private void ListaSom_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemDoSom(e.OriginalSource as DependencyObject) is not { } item || item.IsSelected)
            return;
        listaSom.SelectedItem = listaSom.ItemContainerGenerator.ItemFromContainer(item);
        Logica?.Ouvir();
        e.Handled = true; // sem isto o ComboBox fecha a lista
    }

    // As setas trocam de som e tocam (com a lista aberta ou fechada); o Enter e o Esc fecham a lista, como sempre
    private void ListaSom_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int passo = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (passo == 0 || listaSom.Items.Count == 0 || Keyboard.Modifiers != ModifierKeys.None)
            return;
        int novo = Math.Clamp(listaSom.SelectedIndex + passo, 0, listaSom.Items.Count - 1);
        e.Handled = true;
        if (novo == listaSom.SelectedIndex)
            return;
        listaSom.SelectedIndex = novo;
        if (listaSom.IsDropDownOpen && listaSom.ItemContainerGenerator.ContainerFromIndex(novo) is ComboBoxItem item)
        {
            item.BringIntoView();
            item.Focus();
        }
        Logica?.Ouvir();
    }

    // O item da lista onde o clique caiu (o texto dentro dele, a borda...); null fora dos itens
    private static ComboBoxItem? ItemDoSom(DependencyObject? elemento)
    {
        while (elemento != null && elemento is not ComboBoxItem)
            elemento = elemento is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(elemento)
                : LogicalTreeHelper.GetParent(elemento);
        return elemento as ComboBoxItem;
    }

    private void BotaoEscolherPasta_Click(object sender, RoutedEventArgs e)
    {
        var escolha = new OpenFolderDialog();
        if (escolha.ShowDialog(Window.GetWindow(this)) == true)
            Logica?.MudarPasta(escolha.FolderName);
    }
}
