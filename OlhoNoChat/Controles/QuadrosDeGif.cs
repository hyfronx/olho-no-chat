using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace OlhoNoChat.Controles;

/// <summary>
/// Um GIF animado lido em quadros prontos (cada um já com o que ficou dos anteriores) e a animação que troca um pelo
/// outro, para <see cref="ImagemAnimada"/>. O WPF sozinho só mostra o primeiro quadro de um GIF.
/// </summary>
public sealed class QuadrosDeGif
{
    // Como os navegadores: tempo 0 ou 10 ms (1 centésimo) vira 100 ms
    private const int CentesimosPadrao = 10;

    private QuadrosDeGif(IReadOnlyList<BitmapSource> quadros, IReadOnlyList<TimeSpan> tempos)
    {
        Quadros = quadros;
        Tempos = tempos;
        var animacao = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        TimeSpan inicio = TimeSpan.Zero;
        for (int i = 0; i < quadros.Count; i++)
        {
            animacao.KeyFrames.Add(new DiscreteObjectKeyFrame(quadros[i], KeyTime.FromTimeSpan(inicio)));
            inicio += tempos[i];
        }
        animacao.Duration = inicio;
        animacao.Freeze();
        Animacao = animacao;
    }

    public IReadOnlyList<BitmapSource> Quadros { get; }
    public IReadOnlyList<TimeSpan> Tempos { get; }

    /// <summary>A animação da propriedade Source de uma Image (congelada: pode ser usada por várias imagens).</summary>
    public ObjectAnimationUsingKeyFrames Animacao { get; }

    public BitmapSource Primeiro => Quadros[0];

    /// <summary>
    /// Lê um GIF (pode ser chamado fora da thread da janela: tudo sai congelado). Null se não for um GIF com mais de um
    /// quadro que dê para ler.
    /// </summary>
    public static QuadrosDeGif? Ler(byte[] bytes)
    {
        try
        {
            var decodificador = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decodificador.Frames.Count < 2)
                return null;

            int largura = Numero(decodificador.Metadata, "/logscrdesc/Width") ?? decodificador.Frames[0].PixelWidth;
            int altura = Numero(decodificador.Metadata, "/logscrdesc/Height") ?? decodificador.Frames[0].PixelHeight;
            if (largura <= 0 || altura <= 0)
                return null;

            int passo = largura * 4;
            var tela = new byte[passo * altura]; // BGRA, começa transparente
            var quadros = new List<BitmapSource>(decodificador.Frames.Count);
            var tempos = new List<TimeSpan>(decodificador.Frames.Count);

            foreach (BitmapFrame quadro in decodificador.Frames)
            {
                var info = quadro.Metadata as BitmapMetadata;
                int esquerda = Numero(info, "/imgdesc/Left") ?? 0;
                int topo = Numero(info, "/imgdesc/Top") ?? 0;
                int descarte = Numero(info, "/grctlext/Disposal") ?? 0;
                int centesimos = Numero(info, "/grctlext/Delay") ?? 0;

                byte[]? antes = descarte == 3 ? (byte[])tela.Clone() : null;

                // O pedaço deste quadro por cima do que já estava (pixels transparentes deixam ver o de baixo)
                var bgra = new FormatConvertedBitmap(quadro, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var pedaco = new byte[w * 4 * h];
                bgra.CopyPixels(pedaco, w * 4, 0);
                for (int y = 0; y < h; y++)
                {
                    int ty = topo + y;
                    if (ty < 0 || ty >= altura)
                        continue;
                    for (int x = 0; x < w; x++)
                    {
                        int tx = esquerda + x;
                        if (tx < 0 || tx >= largura)
                            continue;
                        int de = (y * w + x) * 4;
                        if (pedaco[de + 3] == 0)
                            continue;
                        Buffer.BlockCopy(pedaco, de, tela, ty * passo + tx * 4, 4);
                    }
                }

                BitmapSource pronto = BitmapSource.Create(largura, altura, 96, 96, PixelFormats.Bgra32, null, tela, passo);
                pronto.Freeze();
                quadros.Add(pronto);
                tempos.Add(TimeSpan.FromMilliseconds((centesimos <= 1 ? CentesimosPadrao : centesimos) * 10));

                // Depois de mostrado: 2 = o pedaço volta a ser transparente; 3 = volta como estava antes dele
                if (descarte == 2)
                {
                    for (int y = Math.Max(0, topo); y < Math.Min(altura, topo + h); y++)
                    {
                        int x0 = Math.Max(0, esquerda), x1 = Math.Min(largura, esquerda + w);
                        if (x1 > x0)
                            Array.Clear(tela, y * passo + x0 * 4, (x1 - x0) * 4);
                    }
                }
                else if (antes != null)
                {
                    tela = antes;
                }
            }
            return new QuadrosDeGif(quadros, tempos);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or IOException)
        {
            return null;
        }
    }

    private static int? Numero(BitmapMetadata? info, string consulta)
    {
        try
        {
            object? valor = info?.GetQuery(consulta);
            return valor == null ? null : Convert.ToInt32(valor);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or FormatException
                                       or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
