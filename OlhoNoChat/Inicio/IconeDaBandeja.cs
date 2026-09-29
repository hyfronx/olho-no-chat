using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace OlhoNoChat.Inicio;

/// <summary>
/// O ícone do Olho no Chat perto do relógio, sempre presente enquanto o app roda. Clique esquerdo e duplo clique não
/// fazem nada; o direito abre o menu (o único jeito de mostrar as bordas de novo sem atalho). Os atalhos não aparecem no
/// menu. O visual do menu vem dos estilos do app (Estilos/Controles.xaml).
/// </summary>
public sealed class IconeDaBandeja : IDisposable
{
    private readonly TaskbarIcon _icone;
    private readonly MenuItem _sempreNoTopo;

    public IconeDaBandeja()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Olho no Chat", IsEnabled = false });
        menu.Items.Add(Item("Mostrar/esconder bordas", () => BordasPedido?.Invoke()));
        menu.Items.Add(Item("Modo rolagem (rolar o chat com o mouse)", () => ModoRolagemPedido?.Invoke()));
        _sempreNoTopo = Item("Sempre no topo", () => SempreNoTopoPedido?.Invoke());
        menu.Items.Add(_sempreNoTopo);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Configurações", () => ConfiguracoesPedido?.Invoke()));
        menu.Items.Add(Item("Restaurar posição da janela", () => RestaurarPosicaoPedido?.Invoke()));
        menu.Items.Add(Item("Procurar atualizações", () => ProcurarAtualizacoesPedido?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sair", () => SairPedido?.Invoke()));

        _icone = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/icons/olho_no_chat.ico")),
            ToolTipText = "Olho no Chat",
            ContextMenu = menu,
        };
        // Criado fora de uma janela, então precisa ser criado à mão. Sem o "modo de eficiência" do Windows, que deixaria
        // o app todo mais lento (é o que o ForceCreate faz sem o false)
        _icone.ForceCreate(enablesEfficiencyMode: false);
    }

    public event Action? BordasPedido;
    public event Action? ModoRolagemPedido;
    public event Action? SempreNoTopoPedido;
    public event Action? ConfiguracoesPedido;
    public event Action? RestaurarPosicaoPedido;
    public event Action? ProcurarAtualizacoesPedido;
    public event Action? SairPedido;

    /// <summary>A marca de ligado do "Sempre no topo".</summary>
    public void MostrarSempreNoTopo(bool ligado) => _sempreNoTopo.IsChecked = ligado;

    private static MenuItem Item(string texto, Action acao)
    {
        var item = new MenuItem { Header = texto };
        item.Click += (_, _) => acao();
        return item;
    }

    public void Dispose() => _icone.Dispose();
}
