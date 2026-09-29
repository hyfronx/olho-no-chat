using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OlhoNoChat.Controles;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Um emote da lista de emotes: o nome e a imagem, pedida a <see cref="ImagensDeEmotes"/> na primeira vez que aparece
/// (lida fora da thread da janela) e, nos emotes animados, os quadros da animação.
/// </summary>
public sealed class EmoteNaLista : INotifyPropertyChanged
{
    private readonly string _escala;
    private readonly int _larguraGuardada;
    private readonly ImagensDeEmotes? _imagens;
    private ImageSource? _imagem;
    private QuadrosDeGif? _quadros;
    private bool _pedida;

    /// <param name="escala">"1.0" ou "2.0" (telas com escala maior que 120%).</param>
    /// <param name="larguraGuardada">A imagem parada fica guardada do tamanho mostrado (0 = como veio).</param>
    public EmoteNaLista(string id, string nome, bool animado, string escala, int larguraGuardada, ImagensDeEmotes? imagens)
    {
        Id = id;
        Nome = nome;
        Animado = animado;
        _escala = escala;
        _larguraGuardada = larguraGuardada;
        _imagens = imagens;
    }

    public string Id { get; }
    public string Nome { get; }
    public bool Animado { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A imagem parada (nos animados, o primeiro quadro); null até chegar.</summary>
    public ImageSource? Imagem
    {
        get
        {
            if (!_pedida)
            {
                _pedida = true;
                _ = CarregarAsync();
            }
            return _imagem;
        }
    }

    /// <summary>Os quadros da animação (só nos animados, depois que chegam).</summary>
    public QuadrosDeGif? Quadros => _quadros;

    private async Task CarregarAsync()
    {
        if (_imagens == null)
            return;
        // O animado vem primeiro; se não vier (ou não for um GIF com vários quadros), fica o parado
        if (Animado)
        {
            byte[]? gif = await _imagens.ObterAsync(Id, animado: true, _escala);
            QuadrosDeGif? quadros = gif == null ? null : await Task.Run(() => QuadrosDeGif.Ler(gif));
            if (quadros != null)
            {
                _quadros = quadros;
                _imagem = quadros.Primeiro;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Imagem)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quadros)));
                return;
            }
        }

        byte[]? png = await _imagens.ObterAsync(Id, animado: false, _escala);
        if (png == null)
            return;
        ImageSource? imagem = await Task.Run(() => Decodificar(png, _larguraGuardada));
        if (imagem == null)
            return;
        _imagem = imagem;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Imagem)));
    }

    private static BitmapImage? Decodificar(byte[] bytes, int largura)
    {
        try
        {
            var imagem = new BitmapImage();
            imagem.BeginInit();
            imagem.StreamSource = new MemoryStream(bytes);
            imagem.CacheOption = BitmapCacheOption.OnLoad;
            if (largura > 0)
                imagem.DecodePixelWidth = largura;
            imagem.EndInit();
            imagem.Freeze();
            return imagem;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or IOException)
        {
            return null;
        }
    }
}
