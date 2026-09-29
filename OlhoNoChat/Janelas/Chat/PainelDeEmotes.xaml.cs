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
/// quando a caixa de mensagem ganha o foco e fica guardada 10 minutos (<see cref="ListaDeEmotes"/>). Enquanto isso, a
/// última lista conhecida (guardada no disco) já aparece, e só é refeita se a da Twitch vier diferente. As imagens vêm de
/// <see cref="ImagensDeEmotes"/> (guardadas no disco); os emotes animados se mexem enquanto a lista está aberta.
/// </summary>
public partial class PainelDeEmotes : UserControl
{
    private readonly DispatcherTimer _esperaDaBusca = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private ListaDeEmotes? _listaDeEmotes;
    private ImagensDeEmotes? _imagens;
    private ContaDaTwitch? _conta;
    private ILogger? _log;
    private IReadOnlyList<GrupoDeEmotes>? _grupos;
    private string? _assinatura; // da lista de que os grupos foram montados
    private string? _canalDosGrupos;
    private bool _soGlobais;
    private IReadOnlyList<GrupoDeEmotes> _mostrados = []; // os grupos na tela (com a busca)
    private int _colunas;

    // Um botão de emote (BotaoDoEmote) e a barra de rolagem fina (Rolagem.xaml)
    private const double LarguraDoEmote = 38;
    private const double LarguraDaRolagem = 12;

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

    public void Ligar(ListaDeEmotes listaDeEmotes, ImagensDeEmotes imagens, ContaDaTwitch conta, ILogger log)
    {
        _listaDeEmotes = listaDeEmotes;
        _imagens = imagens;
        _conta = conta;
        _log = log;
    }

    /// <summary>A lista está pronta para o canal (não precisa carregar de novo).</summary>
    public bool Pronta(string canal) => _listaDeEmotes?.EstaPronta(canal) == true;

    /// <summary>Esquece a lista (outra conta, ou a permissão nova depois de conectar de novo).</summary>
    public void Descartar()
    {
        _grupos = null; // solta as imagens
        _assinatura = null;
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

        if (_canalDosGrupos != canal)
        {
            _grupos = null;
            _assinatura = null;
        }
        // Enquanto a Twitch responde: a última lista conhecida (da memória ou do disco) ou "Carregando"
        bool mostrandoAConhecida = false;
        if (!_listaDeEmotes.EstaPronta(canal) || _grupos == null)
        {
            if (_listaDeEmotes.UltimaConhecida(canal) is { } conhecida)
            {
                mostrandoAConhecida = true;
                if (Montar(conhecida, canal))
                    MostrarEmotes();
            }
            else
            {
                gruposDeEmotes.ItemsSource = null;
                avisoSoGlobais.Visibility = Visibility.Collapsed;
                MostrarStatus("Carregando emotes…");
            }
        }
        try
        {
            await _conta.VerificarUmaVezAsync();
            ListaDeEmotes.Lista lista = await _listaDeEmotes.BuscarAsync(canal);
            // A nova só é mostrada se mudou (sem voltar a rolagem para o começo quem já está olhando a lista)
            if (Montar(lista, canal))
                MostrarEmotes(voltarAoComeco: !mostrandoAConhecida);
            else if (!mostrandoAConhecida)
                MostrarEmotes();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Não deu para carregar os emotes.");
            if (!mostrandoAConhecida)
                MostrarStatus("Não foi possível carregar os emotes. Confira sua internet e tente de novo.");
        }
    }

    // Monta os grupos da lista, se ela for diferente da que está montada (os emotes que já estavam são aproveitados, com as
    // imagens). Devolve se mudou.
    private bool Montar(ListaDeEmotes.Lista lista, string canal)
    {
        string assinatura = lista.Assinatura;
        if (_grupos != null && assinatura == _assinatura)
            return false;

        // Em telas com escala maior, a imagem do dobro do tamanho, guardada do tamanho mostrado (nítida)
        double escala = VisualTreeHelper.GetDpi(this).DpiScaleX;
        string arquivo = escala > 1.2 ? "2.0" : "1.0";
        int largura = escala > 1.2 && escala < 2.0 ? (int)Math.Ceiling(28 * escala) : 0;
        var existentes = new Dictionary<string, EmoteNaLista>();
        foreach (EmoteNaLista e in _grupos?.SelectMany(g => g.Emotes) ?? [])
            existentes.TryAdd(e.Id + "|" + e.Nome + "|" + e.Animado, e);
        _grupos = lista.Grupos
            .Select(g => new GrupoDeEmotes(g.Titulo, g.Emotes
                .Select(e => existentes.GetValueOrDefault(e.Id + "|" + e.Nome + "|" + e.Animado)
                             ?? new EmoteNaLista(e.Id, e.Nome, e.Animado, arquivo, largura, _imagens))
                .ToList()))
            .ToList();
        _assinatura = assinatura;
        _canalDosGrupos = canal;
        _soGlobais = lista.SoGlobais;
        return true;
    }

    private void MostrarEmotes(bool voltarAoComeco = true)
    {
        if (_grupos == null)
            return;

        string busca = caixaProcurarEmote.Text.Trim();
        _mostrados = GrupoDeEmotes.Filtrar(_grupos, busca);
        _colunas = Colunas();
        gruposDeEmotes.ItemsSource = GrupoDeEmotes.EmLinhas(_mostrados, _colunas);
        if (voltarAoComeco)
            (gruposDeEmotes.Template?.FindName("rolagemDosEmotes", gruposDeEmotes) as ScrollViewer)?.ScrollToTop();
        avisoSoGlobais.Visibility = _soGlobais ? Visibility.Visible : Visibility.Collapsed;
        MostrarStatus(_mostrados.Count > 0 ? null
            : busca.Length > 0 ? $"Nenhum emote com \"{busca}\"."
            : "Nenhum emote para mostrar.");
    }

    // Quantos emotes cabem numa linha (antes de a lista aparecer pela primeira vez, pela largura pedida ao painel)
    private int Colunas()
    {
        double largura = gruposDeEmotes.ActualWidth > 0 ? gruposDeEmotes.ActualWidth : (double.IsNaN(Width) ? ActualWidth : Width) - 18;
        return Math.Max(1, (int)((largura - LarguraDaRolagem) / LarguraDoEmote));
    }

    // Janela mais larga ou mais estreita: as linhas são refeitas se cabe outro número de emotes
    private void Grupos_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_grupos != null && e.WidthChanged && Colunas() != _colunas)
            MostrarEmotes(voltarAoComeco: false);
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
            EmoteNaLista? primeiro = _mostrados.SelectMany(g => g.Emotes).FirstOrDefault();
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
