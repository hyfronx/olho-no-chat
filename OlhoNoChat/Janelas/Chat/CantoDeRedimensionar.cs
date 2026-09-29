#nullable enable
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Redimensionar a janela do chat com as bordas visíveis: a janela não tem moldura do Windows, então ela mesma responde
/// ao Windows qual borda está debaixo do mouse (<see cref="BordasDaJanela"/>). A página do chat é uma janela à parte e
/// fica com o mouse: por isso ela fica a 6 px das bordas, e o quadrado de 16 px de baixo à direita é recortado dela
/// (com os pontinhos desenhados ali) enquanto a página chega até embaixo.
/// </summary>
internal sealed class CantoDeRedimensionar
{
    private const int WM_NCHITTEST = 0x0084;

    private readonly Window _janela;
    private readonly Func<bool> _podeRedimensionar;
    private IntPtr _recortada;

    /// <param name="podeRedimensionar">Bordas visíveis e janela em estado normal.</param>
    public CantoDeRedimensionar(Window janela, Func<bool> podeRedimensionar)
    {
        _janela = janela;
        _podeRedimensionar = podeRedimensionar;
        janela.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(janela).Handle)?.AddHook(AntesDoWpf);
    }

    private IntPtr AntesDoWpf(IntPtr hwnd, int mensagem, IntPtr wParam, IntPtr lParam, ref bool tratada)
    {
        if (mensagem != WM_NCHITTEST || !_podeRedimensionar())
            return IntPtr.Zero;

        long valor = lParam.ToInt64();
        Point ponto = _janela.PointFromScreen(new Point((short)(valor & 0xFFFF), (short)((valor >> 16) & 0xFFFF)));
        int parte = BordasDaJanela.Parte(ponto.X, ponto.Y, _janela.ActualWidth, _janela.ActualHeight);
        if (parte == BordasDaJanela.Nenhuma)
            return IntPtr.Zero;

        tratada = true;
        return new IntPtr(parte);
    }

    /// <summary>
    /// Recorta (ou devolve) o quadrado do canto na janela da página. <paramref name="pagina"/> = o controle do navegador
    /// (null sem navegador); <paramref name="margem"/> = a distância da página até a borda de baixo e a da direita.
    /// </summary>
    public void Atualizar(HwndHost? pagina, bool livre, double margem)
    {
        if (pagina == null || pagina.Handle == IntPtr.Zero)
            return;

        if (!livre)
        {
            if (_recortada == pagina.Handle)
                JanelaDoWindows.TirarRecorte(pagina.Handle);
            _recortada = IntPtr.Zero;
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(_janela);
        int largura = (int)Math.Round(pagina.ActualWidth * dpi.DpiScaleX);
        int altura = (int)Math.Round(pagina.ActualHeight * dpi.DpiScaleY);
        int larguraDoCanto = (int)Math.Ceiling((BordasDaJanela.Canto - margem) * dpi.DpiScaleX);
        int alturaDoCanto = (int)Math.Ceiling((BordasDaJanela.Canto - margem) * dpi.DpiScaleY);

        if (JanelaDoWindows.RecortarCantoDeBaixo(pagina.Handle, largura, altura, larguraDoCanto, alturaDoCanto))
            _recortada = pagina.Handle;
    }
}
