#nullable enable
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using static OlhoNoChat.Sistema.FuncoesDoWindows;

namespace OlhoNoChat.Atalhos;

/// <summary>
/// Os atalhos de teclado que funcionam em qualquer programa (com o jogo em foco). Cada atalho só funciona para um
/// programa por vez: um que outro programa esteja usando agora (outro Olho no Chat aberto antes, por exemplo) é
/// tentado de novo a cada 5 s, e passa a funcionar assim que o outro programa soltar a combinação.
/// </summary>
/// <remarks>
/// Os atalhos chegam a uma janela invisível do Windows (só de mensagens), criada na thread da tela: as ações
/// rodam na thread da tela.
/// </remarks>
public sealed class AtalhosGlobais : IDisposable
{
    private static readonly TimeSpan IntervaloDaNovaTentativa = TimeSpan.FromSeconds(5);
    private static readonly IntPtr JanelaSoDeMensagens = new(-3); // HWND_MESSAGE

    private sealed class Registro
    {
        public required int Id { get; init; }
        public required Atalho Atalho { get; init; }
        public required Action Acao { get; init; }
        public bool Registrado { get; set; }
        public bool AvisouQueEstaOcupado { get; set; }
    }

    private readonly ILogger? _log;
    private readonly HwndSource _janela;
    private readonly DispatcherTimer _novaTentativa;
    private readonly Dictionary<string, Registro> _registros = new();
    private int _proximoId = 1;
    private bool _ligados = true;

    public AtalhosGlobais(ILogger? log = null)
    {
        _log = log;
        _janela = new HwndSource(new HwndSourceParameters("OlhoNoChat.Atalhos") { ParentWindow = JanelaSoDeMensagens, WindowStyle = 0 });
        _janela.AddHook(AoReceberMensagem);
        _novaTentativa = new DispatcherTimer { Interval = IntervaloDaNovaTentativa };
        _novaTentativa.Tick += (_, _) => RegistrarOsQueFaltam();
    }

    /// <summary>
    /// Desligados, os atalhos são devolvidos ao Windows (Configurações aberta: assim a combinação pode ser gravada
    /// na caixa de atalho sem disparar a ação). Ligados de novo, voltam a ser registrados.
    /// </summary>
    public bool Ligados
    {
        get => _ligados;
        set
        {
            if (_ligados == value)
                return;
            _ligados = value;
            if (value)
                RegistrarOsQueFaltam();
            else
                foreach (Registro registro in _registros.Values)
                    Soltar(registro);
        }
    }

    /// <summary>
    /// Liga <paramref name="acao"/> ao atalho com esse nome, trocando o que havia antes. Atalho vazio só solta o
    /// anterior.
    /// </summary>
    public void Definir(string nome, Atalho? atalho, Action acao)
    {
        Remover(nome);
        if (!Atalho.Existe(atalho))
            return;

        // Uma cópia: o atalho das configurações pode mudar depois sem avisar
        var registro = new Registro { Id = _proximoId++, Atalho = atalho with { }, Acao = acao };
        _registros[nome] = registro;
        if (_ligados)
            Registrar(nome, registro);
    }

    public void RemoverTodos()
    {
        foreach (string nome in _registros.Keys.ToList())
            Remover(nome);
    }

    private void Remover(string nome)
    {
        if (!_registros.Remove(nome, out Registro? registro))
            return;
        Soltar(registro);
        if (!_registros.Values.Any(r => !r.Registrado))
            _novaTentativa.Stop();
    }

    private bool Registrar(string nome, Registro registro)
    {
        uint tecla = (uint)KeyInterop.VirtualKeyFromKey(registro.Atalho.Tecla);
        // Os números dos modificadores do WPF são os mesmos do Windows (Alt 1, Ctrl 2, Shift 4, Win 8).
        // Sem repetição: segurar a combinação não liga e desliga várias vezes.
        uint modificadores = (uint)registro.Atalho.Modificadores | MOD_NOREPEAT;

        if (RegisterHotKey(_janela.Handle, registro.Id, modificadores, tecla))
        {
            registro.Registrado = true;
            if (registro.AvisouQueEstaOcupado)
                _log?.LogInformation("Atalho {Nome} ({Atalho}) funcionando agora.", nome, registro.Atalho);
            registro.AvisouQueEstaOcupado = false;
            return true;
        }

        if (!registro.AvisouQueEstaOcupado)
            _log?.LogWarning("Atalho {Nome} ({Atalho}) está em uso por outro programa; tentando de novo a cada {Segundos} s.",
                nome, registro.Atalho, IntervaloDaNovaTentativa.TotalSeconds);
        registro.AvisouQueEstaOcupado = true;
        _novaTentativa.Start();
        return false;
    }

    private void RegistrarOsQueFaltam()
    {
        if (!_ligados)
        {
            _novaTentativa.Stop();
            return;
        }

        bool faltaAlgum = false;
        foreach (var (nome, registro) in _registros)
        {
            if (!registro.Registrado && !Registrar(nome, registro))
                faltaAlgum = true;
        }
        if (!faltaAlgum)
            _novaTentativa.Stop();
    }

    private void Soltar(Registro registro)
    {
        if (registro.Registrado)
            UnregisterHotKey(_janela.Handle, registro.Id);
        registro.Registrado = false;
    }

    private IntPtr AoReceberMensagem(IntPtr hwnd, int mensagem, IntPtr wParam, IntPtr lParam, ref bool tratada)
    {
        if (mensagem != WM_HOTKEY || !_ligados)
            return IntPtr.Zero;

        int id = wParam.ToInt32();
        Registro? registro = _registros.Values.FirstOrDefault(r => r.Id == id);
        if (registro != null)
        {
            tratada = true;
            registro.Acao();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _novaTentativa.Stop();
        foreach (Registro registro in _registros.Values)
            Soltar(registro);
        _registros.Clear();
        _janela.Dispose();
    }
}
