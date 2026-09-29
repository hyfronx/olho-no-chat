using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>Um emote da lista de emotes: o nome e a imagem estática (baixada na primeira vez que aparece).</summary>
public sealed class EmoteNaLista
{
    private readonly string _endereco;
    private readonly int _larguraGuardada;
    private BitmapImage? _imagem;

    /// <param name="escala">"1.0" ou "2.0" (telas com escala maior que 120%).</param>
    /// <param name="larguraGuardada">A imagem fica guardada do tamanho mostrado (0 = como veio).</param>
    public EmoteNaLista(string id, string nome, string escala, int larguraGuardada)
    {
        Nome = nome;
        _endereco = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/{escala}";
        _larguraGuardada = larguraGuardada;
    }

    public string Nome { get; }

    public ImageSource Imagem => _imagem ??= CriarImagem();

    private BitmapImage CriarImagem()
    {
        var imagem = new BitmapImage();
        imagem.BeginInit();
        imagem.UriSource = new Uri(_endereco);
        imagem.CacheOption = BitmapCacheOption.OnLoad;
        if (_larguraGuardada > 0)
            imagem.DecodePixelWidth = _larguraGuardada;
        imagem.EndInit();
        return imagem;
    }
}
