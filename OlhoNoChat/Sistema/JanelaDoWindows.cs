#nullable enable
using System.Runtime.InteropServices;
using static OlhoNoChat.Sistema.FuncoesDoWindows;

namespace OlhoNoChat.Sistema;

/// <summary>
/// O que o app faz com as janelas pelo Windows: deixar o clique atravessar, pôr na frente sem roubar o foco,
/// saber qual janela está em foco e de que programa ela é, recortar um pedaço de uma janela.
/// </summary>
internal static class JanelaDoWindows
{
    /// <summary>Os cliques passam direto para o que está atrás (o jogo).</summary>
    public static void DeixarCliqueAtravessar(IntPtr janela) => MudarEstiloEstendido(janela, liga: WS_EX_TRANSPARENT, desliga: 0);

    /// <summary>A janela volta a receber cliques.</summary>
    public static void TornarClicavel(IntPtr janela) => MudarEstiloEstendido(janela, liga: 0, desliga: WS_EX_TRANSPARENT);

    private static void MudarEstiloEstendido(IntPtr janela, long liga, long desliga)
    {
        long estilo = GetWindowLongPtr(janela, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(janela, GWL_EXSTYLE, new IntPtr((estilo | liga) & ~desliga));
    }

    /// <summary>
    /// Põe a janela acima de todas, inclusive das "sempre na frente". Sem <paramref name="ativar"/> muda só a
    /// ordem: o foco nunca sai do jogo.
    /// </summary>
    public static void ColocarNaFrente(IntPtr janela, bool ativar = false)
    {
        uint opcoes = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | (ativar ? 0 : SWP_NOACTIVATE);
        SetWindowPos(janela, HWND_TOPMOST, 0, 0, 0, 0, opcoes);
    }

    /// <summary>Dá o foco à janela, se o Windows deixar.</summary>
    public static bool DarFoco(IntPtr janela) => SetForegroundWindow(janela);

    public static IntPtr JanelaEmFoco() => GetForegroundWindow();

    public static uint ProcessoDaJanela(IntPtr janela)
    {
        GetWindowThreadProcessId(janela, out uint processo);
        return processo;
    }

    /// <summary>
    /// A janela em foco é de outro programa e está marcada como "sempre na frente" (jogos como o Hunt fazem isso
    /// quando ganham o foco).
    /// </summary>
    public static bool FocoEmOutroProgramaSempreNaFrente()
    {
        IntPtr emFoco = GetForegroundWindow();
        if (emFoco == IntPtr.Zero || ProcessoDaJanela(emFoco) == (uint)Environment.ProcessId)
            return false;

        return (GetWindowLongPtr(emFoco, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
    }

    /// <summary>
    /// Tira da janela (em pixels da tela) o retângulo de baixo à direita com o tamanho dado, para o mouse chegar
    /// à janela que está por baixo. Devolve false se o Windows recusar.
    /// </summary>
    public static bool RecortarCantoDeBaixo(IntPtr janela, int largura, int altura, int larguraDoCanto, int alturaDoCanto)
    {
        IntPtr regiao = CreateRectRgn(0, 0, largura, altura);
        IntPtr canto = CreateRectRgn(largura - larguraDoCanto, altura - alturaDoCanto, largura, altura);
        CombineRgn(regiao, regiao, canto, RGN_DIFF);
        DeleteObject(canto);

        // Dando certo, a região passa a ser da janela e não pode ser apagada aqui
        if (SetWindowRgn(janela, regiao, true) != 0)
            return true;

        DeleteObject(regiao);
        return false;
    }

    public static void TirarRecorte(IntPtr janela) => SetWindowRgn(janela, IntPtr.Zero, true);

    /// <summary>Abre o painel de emojis do Windows (o mesmo de Win + ponto), que escreve na caixa em foco.</summary>
    public static void AbrirPainelDeEmojis()
    {
        const byte VK_LWIN = 0x5B, VK_OEM_PERIOD = 0xBE;
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_OEM_PERIOD, 0, 0, UIntPtr.Zero);
        keybd_event(VK_OEM_PERIOD, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Nome da classe da janela no Windows ("#32770" = caixa de mensagem).</summary>
    public static string ClasseDaJanela(IntPtr janela)
    {
        var nome = new char[64];
        int tamanho = GetClassName(janela, nome, nome.Length);
        return new string(nome, 0, Math.Max(0, tamanho));
    }
}
