using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Som;

namespace OlhoNoChat.Janelas.Configuracoes;

/// <summary>
/// A aba Som: o som de mensagem nova e "Quando tocar" (só no chat Padrão), a saída de som, o volume e a pasta dos sons.
/// "Ouvir" toca o som com a saída e o volume da tela, sem salvar.
/// </summary>
public sealed partial class LogicaSom : LogicaDaPagina
{
    public const string TextoDaPastaPadrao = "Padrão (sons que vêm com o app)";

    private static readonly OpcaoDaLista[] QuandoTocarDaLista =
    [
        new("Em toda mensagem nova", "0"),
        new("No máximo 1 vez a cada 10 s", "10"),
        new("No máximo 1 vez a cada 30 s", "30"),
        new("No máximo 1 vez a cada 1 min", "60"),
        new("No máximo 1 vez a cada 2 min", "120"),
        new("No máximo 1 vez a cada 5 min", "300"),
    ];

    private readonly Action<string?, float, int> _tocarPrevia;

    /// <param name="tocarPrevia">Toca um som (caminho, volume, saída); null = <see cref="TocadorDeAviso.Previa"/>.</param>
    public LogicaSom(Action<string?, float, int>? tocarPrevia = null)
    {
        _tocarPrevia = tocarPrevia ?? TocadorDeAviso.Previa;
    }

    /// <summary>"Nenhum" e os sons da pasta (e o som salvo que sumiu da pasta, como "(não encontrado)").</summary>
    public ObservableCollection<OpcaoDaLista> Sons { get; } = [];

    /// <summary>O arquivo do som escolhido, ou <see cref="SonsDisponiveis.Nenhum"/>.</summary>
    [ObservableProperty]
    private string _somEscolhido = SonsDisponiveis.Nenhum;

    public ObservableCollection<OpcaoDaLista> OpcoesDeQuandoTocar { get; } = [];

    /// <summary>Os segundos entre um som e outro, como texto ("0" = em toda mensagem).</summary>
    [ObservableProperty]
    private string _quandoTocar = "0";

    public IReadOnlyList<TocadorDeAviso.Saida> Saidas { get; private set; } = [];

    [ObservableProperty]
    private int _saidaEscolhida = TocadorDeAviso.Padrao;

    /// <summary>De 0 a 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDoVolume))]
    private double _volume = 100;

    /// <summary>A pasta como fica gravada: <see cref="SonsDisponiveis.PastaPadrao"/> ou o caminho.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDaPasta))]
    private string _pastaDosSons = SonsDisponiveis.PastaPadrao;

    public string TextoDoVolume => $"{Math.Round(Volume):0}%";

    /// <summary>Com uma pasta salva que não existe mais, o app usa os sons que vêm com ele, mas o caminho continua aqui.</summary>
    public string TextoDaPasta => PastaDosSons == SonsDisponiveis.PastaPadrao ? TextoDaPastaPadrao : PastaDosSons;

    public override void Carregar(Opcoes opcoes)
    {
        Saidas = TocadorDeAviso.ListarSaidas();
        OnPropertyChanged(nameof(Saidas));
        // Um aparelho que não é mais o mesmo (outro foi ligado ou tirado) aparece como "Padrão do Windows"
        SaidaEscolhida = TocadorDeAviso.SaidaAindaExiste(opcoes.SaidaDeSom, opcoes.NomeDaSaidaDeSom)
                         && Saidas.Any(s => s.Id == opcoes.SaidaDeSom)
            ? opcoes.SaidaDeSom
            : TocadorDeAviso.Padrao;
        Volume = Math.Clamp(opcoes.Volume * 100, 0, 100);

        PastaDosSons = string.IsNullOrEmpty(opcoes.PastaDosSons) ? SonsDisponiveis.PastaPadrao : opcoes.PastaDosSons;
        ListarSons(opcoes.SomDeMensagem, manterQueSumiu: true);

        OpcoesDeQuandoTocar.Clear();
        foreach (OpcaoDaLista opcao in QuandoTocarDaLista)
            OpcoesDeQuandoTocar.Add(opcao);
        // Um valor que não está na lista (editado à mão) entra nela como item a mais
        string segundos = opcoes.SegundosEntreSons.ToString();
        if (OpcoesDeQuandoTocar.All(o => o.Valor != segundos))
            OpcoesDeQuandoTocar.Add(new OpcaoDaLista($"No máximo 1 vez a cada {segundos} s", segundos));
        QuandoTocar = segundos;
    }

    // "Nenhum" e os sons da pasta. O som escolhido continua se a pasta tiver um arquivo com esse nome. Ao abrir, o som
    // salvo que sumiu da pasta aparece como "(não encontrado)" e salvar sem mexer o mantém (decisão 3); ao trocar de
    // pasta, fica "Nenhum".
    private void ListarSons(string escolhido, bool manterQueSumiu)
    {
        Sons.Clear();
        Sons.Add(new OpcaoDaLista("Nenhum", SonsDisponiveis.Nenhum));
        IReadOnlyList<SonsDisponiveis.Som> daPasta = SonsDisponiveis.Listar(SonsDisponiveis.ResolverPasta(PastaDosSons));

        bool existe = escolhido.Equals(SonsDisponiveis.Nenhum, StringComparison.OrdinalIgnoreCase)
                      || daPasta.Any(s => s.Arquivo == escolhido);
        if (!existe && manterQueSumiu && escolhido.Length > 0)
            Sons.Add(new OpcaoDaLista($"{SonsDisponiveis.NomeMostrado(escolhido)} (não encontrado)", escolhido));

        foreach (SonsDisponiveis.Som som in daPasta)
            Sons.Add(new OpcaoDaLista(som.Nome, som.Arquivo));

        SomEscolhido = Sons.FirstOrDefault(s => s.Valor == escolhido)?.Valor ?? SonsDisponiveis.Nenhum;
    }

    /// <summary>"Escolher pasta..." e "Usar a pasta padrão": a lista de sons muda na hora.</summary>
    public void MudarPasta(string pasta)
    {
        PastaDosSons = string.IsNullOrEmpty(pasta) ? SonsDisponiveis.PastaPadrao : pasta;
        ListarSons(SomEscolhido, manterQueSumiu: false);
    }

    [RelayCommand]
    private void UsarPastaPadrao() => MudarPasta(SonsDisponiveis.PastaPadrao);

    /// <summary>Toca o som escolhido com a saída e o volume da tela ("Nenhum" ou arquivo que não existe: nada).</summary>
    [RelayCommand]
    public void Ouvir() => _tocarPrevia(SonsDisponiveis.Caminho(PastaDosSons, SomEscolhido), VolumeGravado(), SaidaEscolhida);

    private float VolumeGravado() => (float)Math.Clamp(Math.Round(Volume / 100, 2), 0, 1);

    public override void Gravar(Opcoes opcoes)
    {
        TocadorDeAviso.Saida? saida = Saidas.FirstOrDefault(s => s.Id == SaidaEscolhida);
        opcoes.SaidaDeSom = saida?.Id ?? TocadorDeAviso.Padrao;
        opcoes.NomeDaSaidaDeSom = saida == null || saida.Id == TocadorDeAviso.Padrao ? TocadorDeAviso.NomeDaPadraoGravado : saida.Nome;
        opcoes.Volume = VolumeGravado();
        opcoes.PastaDosSons = PastaDosSons;
        opcoes.SegundosEntreSons = int.TryParse(QuandoTocar, out int segundos) ? segundos : 0;

        // Os outros tipos de chat não têm som de mensagem: a escolha salva continua a mesma
        if (TipoNaTela == TipoDeChat.Padrao)
            opcoes.SomDeMensagem = SomEscolhido;
    }

    public override void Estado(IDictionary<string, string> estado)
    {
        estado["Som.SomDeMensagem"] = SomEscolhido;
        estado["Som.QuandoTocar"] = QuandoTocar;
        estado["Som.SaidaDeSom"] = SaidaEscolhida.ToString();
        estado["Som.Volume"] = Math.Round(Volume).ToString();
        estado["Som.PastaDosSons"] = PastaDosSons;
    }
}
