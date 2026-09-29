using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OlhoNoChat.Controles;

namespace OlhoNoChat.Testes.Controles;

public class QuadrosDeGifTestes
{
    // Um GIF de 2x2 com um quadro de cada cor (o GifBitmapEncoder do Windows não grava tempo: fica o padrão)
    private static byte[] Gif(params Color[] cores)
    {
        var gif = new GifBitmapEncoder();
        foreach (Color cor in cores)
        {
            var pixels = Enumerable.Repeat(new[] { cor.B, cor.G, cor.R, (byte)255 }, 4).SelectMany(p => p).ToArray();
            gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8)));
        }
        using var saida = new MemoryStream();
        gif.Save(saida);
        return saida.ToArray();
    }

    private static Color PrimeiroPixel(BitmapSource imagem)
    {
        var bgra = new byte[4];
        new FormatConvertedBitmap(imagem, PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), bgra, 4, 0);
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }

    [Fact]
    public void Ler_UmQuadroProntoPorQuadroDoGifComOTempoPadrao()
    {
        QuadrosDeGif? quadros = QuadrosDeGif.Ler(Gif(Colors.Red, Colors.Blue, Colors.Lime));

        Assert.NotNull(quadros);
        Assert.Equal(3, quadros.Quadros.Count);
        Assert.All(quadros.Quadros, q => Assert.True(q.IsFrozen));
        Assert.Equal((2, 2), (quadros.Primeiro.PixelWidth, quadros.Primeiro.PixelHeight));
        Assert.Equal(Colors.Red, PrimeiroPixel(quadros.Quadros[0]));
        Assert.Equal(Colors.Blue, PrimeiroPixel(quadros.Quadros[1]));
        Assert.Equal(Colors.Lime, PrimeiroPixel(quadros.Quadros[2]));
        Assert.All(quadros.Tempos, t => Assert.Equal(TimeSpan.FromMilliseconds(100), t));
    }

    [Fact]
    public void Ler_AAnimacaoTrocaOsQuadrosNaOrdemERepete()
    {
        QuadrosDeGif quadros = QuadrosDeGif.Ler(Gif(Colors.Red, Colors.Blue))!;

        Assert.True(quadros.Animacao.IsFrozen);
        Assert.Equal(TimeSpan.FromMilliseconds(200), quadros.Animacao.Duration.TimeSpan);
        Assert.True(quadros.Animacao.RepeatBehavior == System.Windows.Media.Animation.RepeatBehavior.Forever);
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(100)], quadros.Animacao.KeyFrames.Cast<System.Windows.Media.Animation.ObjectKeyFrame>().Select(k => k.KeyTime.TimeSpan));
        Assert.Same(quadros.Quadros[1], quadros.Animacao.KeyFrames[1].Value);
    }

    [Fact]
    public void Ler_GifDeUmQuadroOuArquivoQueNaoEGifDaNull()
    {
        Assert.Null(QuadrosDeGif.Ler(Gif(Colors.Red)));
        Assert.Null(QuadrosDeGif.Ler([1, 2, 3, 4]));
        Assert.Null(QuadrosDeGif.Ler([]));
    }
}
