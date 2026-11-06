using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.Janelas.NovaVersao;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace OlhoNoChat.Atualizacoes;

/// <summary>O que a procura de atualizações está fazendo agora (o botão "Procurar agora" da aba Geral mostra).</summary>
public enum EstadoDaProcura
{
    Parada,
    Procurando,
    /// <summary>Depois de "Atualizar agora", até o app reiniciar.</summary>
    Baixando,
}

/// <summary>
/// Procura uma versão nova nas releases do GitHub, mostra a janela "Nova versão disponível" e, em "Atualizar agora",
/// baixa, grava tudo e reinicia o app já atualizado (pelo Velopack).
/// </summary>
/// <remarks>
/// A procura automática (ao abrir o app) fica quieta quando não há versão nova ou quando dá erro; a manual ("Procurar
/// agora", menu perto do relógio) sempre responde. No app de teste (Debug) a procura é numa pasta local
/// (<see cref="InfoDoApp.PastaDoTesteDeAtualizacao"/>), e rodando direto da compilação a janela aparece com versões
/// de exemplo, só para ver.
/// </remarks>
public sealed class ProcuraDeAtualizacoes
{
    private readonly ILogger<ProcuraDeAtualizacoes> _log;
    private readonly Func<Opcoes> _opcoes;
    private readonly Func<bool> _gravar;
    private Window? _janelaAberta;

    /// <param name="opcoes">As opções em uso ("Restaurar tudo para o padrão" troca o objeto).</param>
    /// <param name="gravar">Grava as opções no arquivo.</param>
    public ProcuraDeAtualizacoes(ILogger<ProcuraDeAtualizacoes> log, Func<Opcoes> opcoes, Func<bool> gravar)
    {
        _log = log;
        _opcoes = opcoes;
        _gravar = gravar;
    }

    /// <summary>"Não procurar atualizações automaticamente" desligou a opção (já gravada): a aba Geral aberta acompanha.</summary>
    public event Action? ProcuraAutomaticaDesligada;

    /// <summary>Chamado logo antes do Velopack fechar o app para atualizar (a posição da janela do chat vai para as opções).</summary>
    public Action? AntesDeReiniciar { get; set; }

    /// <summary>A procura em andamento, venha de onde vier (botão, menu perto do relógio ou abertura do app).</summary>
    public EstadoDaProcura Estado { get; private set; }

    /// <summary><see cref="Estado"/> mudou (sempre na thread da interface, de onde a procura é pedida).</summary>
    public event Action? EstadoMudou;

    /// <summary>
    /// Procura agora. <paramref name="dono"/> é a janela de onde veio o pedido (Configurações), que fica atrás do aviso e
    /// das mensagens. Pedir de novo enquanto uma procura está em andamento só traz o aviso aberto para a frente.
    /// </summary>
    public async Task ProcurarAsync(bool manual, Window? dono)
    {
        if (Estado != EstadoDaProcura.Parada)
        {
            _janelaAberta?.Activate();
            return;
        }

        Mudar(EstadoDaProcura.Procurando);
        try
        {
            await ProcurarAgoraAsync(manual, dono);
        }
        finally
        {
            Mudar(EstadoDaProcura.Parada);
            _janelaAberta = null;
        }
    }

    private void Mudar(EstadoDaProcura estado)
    {
        Estado = estado;
        EstadoMudou?.Invoke();
    }

    private async Task ProcurarAgoraAsync(bool manual, Window? dono)
    {
        // O canal escolhido ("Receber versões beta"). Saindo da beta, a última normal vale mesmo sendo menor (volta para
        // ela); entrando na beta, só uma beta mais nova que a versão instalada (nunca volta para trás)
        bool beta = NaBeta(_opcoes());
        var gerenciador = new UpdateManager(Fonte(beta), new UpdateOptions
        {
            ExplicitChannel = beta ? CanalBeta : CanalNormal,
            AllowVersionDowngrade = !beta && InstaladoNaBeta,
        });

#if DEBUG
        // Rodando direto da compilação não há o que atualizar: só o visual da janela, com versões de exemplo
        if (!gerenciador.IsInstalled)
        {
            MostrarAviso("1.1.0", "1.1.5", dono);
            return;
        }
#endif

        UpdateInfo? nova;
        try
        {
            _log.LogInformation("Procurando atualizações...");
            nova = await gerenciador.CheckForUpdatesAsync();
            // Na beta, uma versão normal mais nova que a última beta também vale (a versão final de uma beta sai só como
            // normal): quem entrou pelo instalador da beta passa a seguir as normais; quem ligou a opção continua na beta
            if (beta)
            {
                var normal = new UpdateManager(Fonte(false), new UpdateOptions { ExplicitChannel = CanalNormal });
                UpdateInfo? novaNormal = await normal.CheckForUpdatesAsync();
                if (novaNormal != null && (nova == null || novaNormal.TargetFullRelease.Version > nova.TargetFullRelease.Version))
                {
                    gerenciador = normal;
                    nova = novaNormal;
                }
            }
        }
        catch (Exception ex)
        {
            Falhou(ex, mostrar: manual, dono);
            return;
        }

        if (nova == null)
        {
            if (manual)
                Mensagem(dono, "Você já está com a versão mais recente!", "Sem atualizações", MessageBoxImage.Information);
            return;
        }

        string atual = gerenciador.CurrentVersion?.ToString() ?? "0.0.0";
        if (!MostrarAviso(atual, nova.TargetFullRelease.Version.ToString(), dono))
            return;

        try
        {
            _log.LogInformation("Baixando a versão {Versao}...", nova.TargetFullRelease.Version);
            Mudar(EstadoDaProcura.Baixando);
            await gerenciador.DownloadUpdatesAsync(nova);

            // O Velopack fecha o app sem passar pela gravação normal da saída: tudo é gravado antes
            AntesDeReiniciar?.Invoke();
            _gravar();
            gerenciador.ApplyUpdatesAndRestart(nova);
        }
        catch (Exception ex)
        {
            // A pessoa pediu para atualizar: o erro sempre aparece
            Falhou(ex, mostrar: true, dono);
        }
    }

    // true = "Atualizar agora"
    private bool MostrarAviso(string versaoAtual, string versaoNova, Window? dono)
    {
        var logica = new LogicaNovaVersao(versaoAtual, versaoNova);
        var janela = new JanelaNovaVersao(logica, Application.Current?.MainWindow) { Owner = dono };
        _janelaAberta = janela;
        bool atualizar = janela.ShowDialog() == true;
        _janelaAberta = null;

        if (!atualizar)
            DepoisDoAviso(logica.NaoProcurarAutomaticamente);
        return atualizar;
    }

    /// <summary>
    /// "Depois", Esc ou "×" no aviso: com "Não procurar atualizações automaticamente" marcada, a opção é desligada e
    /// gravada na hora.
    /// </summary>
    public void DepoisDoAviso(bool naoProcurarAutomaticamente)
    {
        Opcoes opcoes = _opcoes();
        if (!naoProcurarAutomaticamente || !opcoes.ProcurarAtualizacoes)
            return;

        _log.LogInformation("Procura automática de atualizações desligada no aviso.");
        opcoes.ProcurarAtualizacoes = false;
        _gravar();
        ProcuraAutomaticaDesligada?.Invoke();
    }

    private void Falhou(Exception ex, bool mostrar, Window? dono)
    {
        _log.LogError(ex, "Erro ao procurar ou baixar atualizações");
        if (ex.InnerException != null)
            _log.LogError(ex.InnerException, "Detalhes do erro");

        // Na abertura do app o erro é quieto (sem internet, nenhuma versão publicada ainda...)
        if (mostrar)
        {
            Mensagem(dono, "Não foi possível procurar atualizações agora. Confira sua internet e tente de novo mais tarde.\n\nDetalhes: " + ex.Message,
                "Procurar atualizações", MessageBoxImage.Warning);
        }
    }

    // Com dono, a mensagem fica na frente dele (Configurações fica sempre no topo)
    private static void Mensagem(Window? dono, string texto, string titulo, MessageBoxImage icone)
    {
        if (dono != null)
            MessageBox.Show(dono, texto, titulo, MessageBoxButton.OK, icone);
        else
            MessageBox.Show(texto, titulo, MessageBoxButton.OK, icone);
    }

    /// <summary>O canal das versões normais (o padrão do Velopack no Windows).</summary>
    public const string CanalNormal = "win";

    /// <summary>O canal das versões beta (as releases marcadas como pré-lançamento no GitHub).</summary>
    public const string CanalBeta = "beta";

    /// <summary>O app foi instalado (ou atualizado por último) pelo canal beta.</summary>
    public static bool InstaladoNaBeta => VelopackLocator.IsCurrentSet && VelopackLocator.Current.Channel == CanalBeta;

    /// <summary>Recebe as versões beta: a escolha da aba Geral, ou, sem escolha, o canal com que foi instalado.</summary>
    public static bool NaBeta(Opcoes opcoes) => opcoes.VersoesBeta ?? InstaladoNaBeta;

    // Na beta, as releases de pré-lançamento entram na procura
    private static IUpdateSource Fonte(bool beta)
    {
#if DEBUG
        return new SimpleFileSource(new DirectoryInfo(InfoDoApp.PastaDoTesteDeAtualizacao));
#else
        return new GithubSource(InfoDoApp.EnderecoDoRepositorio, null, beta);
#endif
    }
}
