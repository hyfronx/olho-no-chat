using static OlhoNoChat.Sistema.FuncoesDoWindows;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Avisa toda vez que uma janela de outro programa ganha o foco (o jogo, por exemplo). O aviso chega na
/// thread da tela, porque o gancho do Windows é criado nela.
/// </summary>
internal sealed class VigiaDeFoco : IDisposable
{
    private readonly AoMudarJanela _aoMudar; // guardado num campo para o coletor de lixo não levar embora
    private IntPtr _gancho;

    public event Action? OutroProgramaGanhouOFoco;

    public VigiaDeFoco()
    {
        _aoMudar = (gancho, evento, janela, objeto, filho, thread, hora) => OutroProgramaGanhouOFoco?.Invoke();
        _gancho = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _aoMudar, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Dispose()
    {
        if (_gancho == IntPtr.Zero)
            return;
        UnhookWinEvent(_gancho);
        _gancho = IntPtr.Zero;
    }
}
