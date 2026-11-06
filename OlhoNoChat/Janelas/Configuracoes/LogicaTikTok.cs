using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba TikTok: a conta do TikTok, opcional, para ler o chat como quem está logado. Conectar e desconectar valem na
/// hora (sem "Salvar"); a aba não tem opções para gravar.
/// </summary>
public sealed partial class LogicaTikTok : LogicaDaPagina
{
    private readonly ContaDoTikTok _conta;
    // A conta muda em outras threads (o TikTok recusou a sessão): a tela é atualizada na da janela
    private readonly SynchronizationContext? _tela = SynchronizationContext.Current;
    private bool _desconectouAgora;

    public LogicaTikTok(ContaDoTikTok conta)
    {
        _conta = conta;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MultiplataformaNaTela))
                MostrarConta();
        };
        MostrarConta();
    }

    /// <summary>Abre a janela de login do TikTok e devolve a conta que entrou (ou null). A página põe a janela dona.</summary>
    public Func<SessaoDoTikTok?>? AbrirLogin { get; set; }

    [ObservableProperty]
    private string _tituloDaConta = string.Empty;

    [ObservableProperty]
    private string _dicaDaConta = string.Empty;

    [ObservableProperty]
    private bool _conectada;

    /// <summary>O TikTok não aceitou mais a sessão: aviso até conectar de novo.</summary>
    [ObservableProperty]
    private bool _foiRecusada;

    /// <summary>Acabou de desconectar: como encerrar a sessão de vez no TikTok.</summary>
    [ObservableProperty]
    private bool _mostrarComoEncerrar;

    /// <summary>Sem o Chat Multiplataforma ligado, a conta não é usada.</summary>
    public bool MostrarAvisoMultiplataforma => !MultiplataformaNaTela;

    /// <summary>Acompanha a conta enquanto a aba está aberta.</summary>
    public void Ligar() => _conta.Mudou += ContaMudou;

    public void Desligar() => _conta.Mudou -= ContaMudou;

    private void ContaMudou()
    {
        if (_tela != null && SynchronizationContext.Current != _tela)
            _tela.Post(_ => MostrarConta(), null);
        else
            MostrarConta();
    }

    [RelayCommand]
    private void Conectar()
    {
        if (AbrirLogin?.Invoke() is not { } conta)
            return;
        _desconectouAgora = false;
        _conta.Guardar(conta);
        MostrarConta();
    }

    [RelayCommand]
    private void Desconectar()
    {
        _desconectouAgora = true;
        _conta.Desconectar();
        MostrarConta();
    }

    private void MostrarConta()
    {
        SessaoDoTikTok? conta = _conta.Conta;
        Conectada = conta != null;
        FoiRecusada = conta == null && _conta.FoiRecusada;
        MostrarComoEncerrar = conta == null && _desconectouAgora;
        OnPropertyChanged(nameof(MostrarAvisoMultiplataforma));
        if (conta == null)
        {
            TituloDaConta = "Nenhuma conta conectada";
            DicaDaConta = "O chat do TikTok é lido sem conta.";
            return;
        }
        TituloDaConta = "@" + conta.Login;
        DicaDaConta = _conta.Protecao == ProtecaoDaSessao.WindowsETpm
            ? "Sessão protegida pelo Windows e pelo chip de segurança (TPM) deste computador."
            : "Sessão protegida pelo Windows (este computador não tem o chip de segurança TPM).";
    }

    // Sem opções para gravar: conectar e desconectar já valem na hora
    public override void Carregar(Opcoes opcoes)
    {
    }

    public override void Gravar(Opcoes opcoes)
    {
    }

    public override void Estado(IDictionary<string, string> estado)
    {
    }
}
