using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Janelas.Filtros;

/// <summary>
/// A janela Filtros do chat sem a tela: a lista de usuários e o que fazer com ela, moderadores e VIPs, as pessoas
/// bloqueadas e as três cores. Nada vai para as opções antes de <see cref="Salvar"/>.
/// </summary>
public sealed partial class LogicaFiltros : ObservableObject
{
    private readonly Opcoes _opcoes;
    private readonly Func<bool> _gravar;
    private string _estadoSalvo;

    /// <param name="opcoes">As opções do app (lidas agora, escritas só no <see cref="Salvar"/>).</param>
    /// <param name="gravar">Grava as opções no arquivo.</param>
    public LogicaFiltros(Opcoes opcoes, Func<bool> gravar)
    {
        _opcoes = opcoes;
        _gravar = gravar;

        // "Mostrar só" e "Destacar" nunca ficam juntos: com as duas no arquivo, vale "Mostrar só"
        _modo = opcoes.SoUsuariosDaLista ? ModoDaLista.SoALista
            : opcoes.DestacarUsuarios ? ModoDaLista.Destacar
            : ModoDaLista.Nada;
        _destacarModeradores = opcoes.DestacarModeradores;
        _destacarVips = opcoes.DestacarVips;
        ListaDeUsuarios = new ObservableCollection<string>(opcoes.ListaDeUsuarios);
        UsuariosBloqueados = new ObservableCollection<string>(opcoes.UsuariosBloqueados);
        CorDoDestaque = new EscolhaDeCor(opcoes.CorDoDestaque);
        CorDosModeradores = new EscolhaDeCor(opcoes.CorDosModeradores);
        CorDosVips = new EscolhaDeCor(opcoes.CorDosVips);

        ListaDeUsuarios.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ListaVazia));
        UsuariosBloqueados.CollectionChanged += (_, _) => OnPropertyChanged(nameof(BloqueadosVazia));
        MostrarCoresConformeOModo();
        _estadoSalvo = Estado();
    }

    /// <summary>Depois de gravar: o chat aplica os filtros na hora.</summary>
    public event Action? Salvou;

    // ----- Página Usuários -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModoNada), nameof(ModoDestacar), nameof(ModoSoALista), nameof(ModeradoresEVipsAtivos),
        nameof(RotuloModeradores), nameof(RotuloVips))]
    private ModoDaLista _modo;

    // As três opções de escolha única da tela
    public bool ModoNada { get => Modo == ModoDaLista.Nada; set { if (value) Modo = ModoDaLista.Nada; } }
    public bool ModoDestacar { get => Modo == ModoDaLista.Destacar; set { if (value) Modo = ModoDaLista.Destacar; } }
    public bool ModoSoALista { get => Modo == ModoDaLista.SoALista; set { if (value) Modo = ModoDaLista.SoALista; } }

    [ObservableProperty]
    private bool _destacarModeradores;

    [ObservableProperty]
    private bool _destacarVips;

    /// <summary>No modo "Nada" as linhas de moderadores e VIPs ficam desbotadas e desativadas.</summary>
    public bool ModeradoresEVipsAtivos => Modo != ModoDaLista.Nada;

    public string RotuloModeradores => Modo == ModoDaLista.SoALista ? "Mostrar também todos os moderadores" : "Destacar todos os moderadores";

    public string RotuloVips => Modo == ModoDaLista.SoALista ? "Mostrar também todos os VIPs" : "Destacar todos os VIPs";

    public EscolhaDeCor CorDoDestaque { get; }
    public EscolhaDeCor CorDosModeradores { get; }
    public EscolhaDeCor CorDosVips { get; }

    public ObservableCollection<string> ListaDeUsuarios { get; }

    public bool ListaVazia => ListaDeUsuarios.Count == 0;

    /// <summary>O nome digitado na caixa da lista de usuários (ainda não adicionado).</summary>
    [ObservableProperty]
    private string _novoUsuario = string.Empty;

    /// <summary>Por que o nome digitado não entrou (null = sem erro).</summary>
    [ObservableProperty]
    private string? _erroNovoUsuario;

    // As cores só valem para destacar: a do destaque some fora do "Destacar", as de moderadores e VIPs no "Mostrar só"
    partial void OnModoChanged(ModoDaLista value) => MostrarCoresConformeOModo();

    private void MostrarCoresConformeOModo()
    {
        CorDoDestaque.Visivel = Modo == ModoDaLista.Destacar;
        CorDosModeradores.Visivel = Modo != ModoDaLista.SoALista;
        CorDosVips.Visivel = Modo != ModoDaLista.SoALista;
    }

    [RelayCommand]
    private void AdicionarUsuario()
    {
        if (Adicionar(NovoUsuario, ListaDeUsuarios, out string? erro))
            NovoUsuario = string.Empty;
        ErroNovoUsuario = erro;
    }

    /// <summary>O "×" de uma ficha tira o nome só desta lista (o mesmo nome pode estar nas duas).</summary>
    [RelayCommand]
    private void TirarUsuario(string nome) => ListaDeUsuarios.Remove(nome);

    // ----- Página Bloqueios -----

    public ObservableCollection<string> UsuariosBloqueados { get; }

    public bool BloqueadosVazia => UsuariosBloqueados.Count == 0;

    [ObservableProperty]
    private string _novoBloqueado = string.Empty;

    [ObservableProperty]
    private string? _erroNovoBloqueado;

    [RelayCommand]
    private void Bloquear()
    {
        if (Adicionar(NovoBloqueado, UsuariosBloqueados, out string? erro))
            NovoBloqueado = string.Empty;
        ErroNovoBloqueado = erro;
    }

    [RelayCommand]
    private void Desbloquear(string nome) => UsuariosBloqueados.Remove(nome);

    // Aceita "nome", "@nome", "twitch.tv/nome" ou o link do canal. O nome entra com as maiúsculas digitadas.
    private static bool Adicionar(string digitado, ObservableCollection<string> lista, out string? erro)
    {
        string nome = NomesDaTwitch.Extrair(digitado);

        if (nome.Length == 0)
            erro = "Digite o nome do usuário.";
        else if (!NomesDaTwitch.EhValido(nome))
            erro = NomesDaTwitch.DicaNomeInvalido;
        else if (lista.Any(u => string.Equals(u, nome, StringComparison.OrdinalIgnoreCase)))
            erro = nome + " já está na lista.";
        else
            erro = null;

        if (erro != null)
            return false;
        lista.Add(nome);
        return true;
    }

    // ----- Salvar e mudanças -----

    /// <summary>O que está na tela é diferente do que foi salvo (o nome digitado e não adicionado não conta).</summary>
    public bool TemMudancas => Estado() != _estadoSalvo;

    private string Estado() => string.Join("\u001F",
        Modo, DestacarModeradores, DestacarVips,
        string.Join(",", ListaDeUsuarios), string.Join(",", UsuariosBloqueados),
        CorDoDestaque.Cor, CorDosModeradores.Cor, CorDosVips.Cor);

    /// <summary>
    /// Grava tudo e avisa o chat. Um nome digitado e não adicionado entra antes (se não valer, o erro aparece e o resto
    /// é salvo mesmo assim); uma cor digitada que não vale mostra o erro e fica a cor salva antes.
    /// </summary>
    public void Salvar()
    {
        if (NovoUsuario.Trim().Length > 0)
            AdicionarUsuario();
        if (NovoBloqueado.Trim().Length > 0)
            Bloquear();

        _opcoes.DestacarUsuarios = Modo == ModoDaLista.Destacar;
        _opcoes.SoUsuariosDaLista = Modo == ModoDaLista.SoALista;
        _opcoes.DestacarModeradores = DestacarModeradores;
        _opcoes.DestacarVips = DestacarVips;
        _opcoes.ListaDeUsuarios = [.. ListaDeUsuarios];
        _opcoes.UsuariosBloqueados = [.. UsuariosBloqueados];
        _opcoes.CorDoDestaque = CorDoDestaque.Salvar();
        _opcoes.CorDosModeradores = CorDosModeradores.Salvar();
        _opcoes.CorDosVips = CorDosVips.Salvar();
        _gravar();

        _estadoSalvo = Estado();
        Salvou?.Invoke();
    }
}
