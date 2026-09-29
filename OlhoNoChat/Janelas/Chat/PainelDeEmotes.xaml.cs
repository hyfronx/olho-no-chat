using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// A lista de emotes da caixa de escrever: os que a conta conectada pode usar neste canal (do canal, das inscrições e os
/// globais), com a busca, o botão do painel de emojis do Windows e, para acessos antigos sem a permissão de ler os
/// emotes, o aviso com "Conectar de novo". A Twitch manda a lista em muitas páginas pequenas: ela começa a carregar
/// quando a caixa de mensagem ganha o foco e fica guardada 10 minutos (<see cref="ListaDeEmotes"/>).
/// </summary>
public partial class PainelDeEmotes : UserControl
{
    private readonly DispatcherTimer _esperaDaBusca = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private ListaDeEmotes? _listaDeEmotes;
    private ContaDaTwitch? _conta;
    private ILogger? _log;
    private IReadOnlyList<GrupoDeEmotes>? _grupos;
    private ListaDeEmotes.Lista? _deOnde; // a lista de que os grupos foram montados
    private bool _soGlobais;

    public PainelDeEmotes()
    {
        InitializeComponent();
        // A lista grande demora um pouco para ser montada: filtra quando a digitação para
        _esperaDaBusca.Tick += (_, _) =>
        {
            _esperaDaBusca.Stop();
            MostrarEmotes();
        };
    }

    /// <summary>Um emote foi clicado (ou Enter na busca): o nome vai para a mensagem.</summary>
    public event Action<string>? EmoteEscolhido;

    public event Action? EmojisPedido;
    public event Action? ConectarDeNovoPedido;

    /// <summary>Esc na busca.</summary>
    public event Action? FecharPedido;

    public void Ligar(ListaDeEmotes listaDeEmotes, ContaDaTwitch conta, ILogger log)
    {
        _listaDeEmotes = listaDeEmotes;
        _conta = conta;
        _log = log;
    }

    /// <summary>A lista está pronta para o canal (não precisa carregar de novo).</summary>
    public bool Pronta(string canal) => _listaDeEmotes?.EstaPronta(canal) == true;

    /// <summary>Esquece a lista (outra conta, ou a permissão nova depois de conectar de novo).</summary>
    public void Descartar()
    {
        _grupos = null; // solta as imagens
        _listaDeEmotes?.Descartar();
    }

    /// <summary>Ao abrir: a busca volta vazia e a lista inteira aparece.</summary>
    public void LimparBusca()
    {
        caixaProcurarEmote.Text = string.Empty;
        _esperaDaBusca.Stop();
    }

    public void AoFechar() => _esperaDaBusca.Stop();

    /// <summary>Carrega a lista do canal (uma busca em andamento, a do foco na caixa, é aproveitada).</summary>
    public async Task CarregarAsync(string canal)
    {
        if (_listaDeEmotes == null || _conta == null)
            return;

        if (!_listaDeEmotes.EstaPronta(canal) || _grupos == null)
        {
            gruposDeEmotes.ItemsSource = null;
            avisoSoGlobais.Visibility = Visibility.Collapsed;
            MostrarStatus("Carregando emotes…");
        }
        try
        {
            await _conta.VerificarUmaVezAsync();
            ListaDeEmotes.Lista lista = await _listaDeEmotes.BuscarAsync(canal);
            if (_grupos == null || !ReferenceEquals(lista, _deOnde))
            {
                // Em telas com escala maior, a imagem do dobro do tamanho, guardada do tamanho mostrado (nítida)
                double escala = VisualTreeHelper.GetDpi(this).DpiScaleX;
                string arquivo = escala > 1.2 ? "2.0" : "1.0";
                int largura = escala > 1.2 && escala < 2.0 ? (int)Math.Ceiling(28 * escala) : 0;
                _grupos = lista.Grupos
                    .Select(g => new GrupoDeEmotes(g.Titulo, g.Emotes.Select(e => new EmoteNaLista(e.Id, e.Nome, arquivo, largura)).ToList()))
                    .ToList();
                _deOnde = lista;
                _soGlobais = lista.SoGlobais;
            }
            MostrarEmotes();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Não deu para carregar os emotes.");
            MostrarStatus("Não foi possível carregar os emotes. Confira sua internet e tente de novo.");
        }
    }

    private void MostrarEmotes()
    {
        if (_grupos == null)
            return;

        string busca = caixaProcurarEmote.Text.Trim();
        IReadOnlyList<GrupoDeEmotes> grupos = GrupoDeEmotes.Filtrar(_grupos, busca);
        gruposDeEmotes.ItemsSource = grupos;
        rolagemDosEmotes.ScrollToTop();
        avisoSoGlobais.Visibility = _soGlobais ? Visibility.Visible : Visibility.Collapsed;
        MostrarStatus(grupos.Count > 0 ? null
            : busca.Length > 0 ? $"Nenhum emote com \"{busca}\"."
            : "Nenhum emote para mostrar.");
    }

    private void MostrarStatus(string? texto)
    {
        statusEmotes.Text = texto ?? string.Empty;
        statusEmotes.Visibility = string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Procurar_TextChanged(object sender, TextChangedEventArgs e)
    {
        _esperaDaBusca.Stop();
        _esperaDaBusca.Start();
    }

    private void Procurar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FecharPedido?.Invoke();
        }
        else if (e.Key == Key.Enter)
        {
            // O primeiro emote achado vai para a mensagem (do texto digitado, também antes da pausa)
            e.Handled = true;
            if (_esperaDaBusca.IsEnabled)
            {
                _esperaDaBusca.Stop();
                MostrarEmotes();
            }
            EmoteNaLista? primeiro = (gruposDeEmotes.ItemsSource as IEnumerable<GrupoDeEmotes>)?.SelectMany(g => g.Emotes).FirstOrDefault();
            if (primeiro != null)
                EmoteEscolhido?.Invoke(primeiro.Nome);
        }
    }

    private void Emote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmoteNaLista emote)
            EmoteEscolhido?.Invoke(emote.Nome);
    }

    private void Emojis_Click(object sender, RoutedEventArgs e) => EmojisPedido?.Invoke();

    private void ConectarDeNovo_Click(object sender, RoutedEventArgs e) => ConectarDeNovoPedido?.Invoke();
}
