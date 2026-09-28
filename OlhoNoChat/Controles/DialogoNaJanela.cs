#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Threading;

namespace OlhoNoChat.Controles;

/// <summary>
/// Uma pergunta dentro da própria janela ("Salvar as alterações?", "Restaurar tudo para o padrão?"): escurece a
/// janela e mostra um cartão com o título, o texto e até três botões. O visual está em Estilos/Controles.xaml.
/// </summary>
/// <remarks>
/// A janela que o usa encaminha o Esc para <see cref="Responder"/> com <see cref="RespostaDoDialogo.Fechar"/>: o Esc
/// fecha só o diálogo, nunca a janela por baixo.
/// </remarks>
[TemplatePart(Name = "botaoPrincipal", Type = typeof(Button))]
[TemplatePart(Name = "botaoSecundario", Type = typeof(Button))]
[TemplatePart(Name = "botaoFecharDialogo", Type = typeof(Button))]
public class DialogoNaJanela : Control
{
    public static readonly DependencyProperty TituloProperty =
        DependencyProperty.Register(nameof(Titulo), typeof(string), typeof(DialogoNaJanela), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextoProperty =
        DependencyProperty.Register(nameof(Texto), typeof(string), typeof(DialogoNaJanela), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextoPrincipalProperty =
        DependencyProperty.Register(nameof(TextoPrincipal), typeof(string), typeof(DialogoNaJanela), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextoSecundarioProperty =
        DependencyProperty.Register(nameof(TextoSecundario), typeof(string), typeof(DialogoNaJanela), new PropertyMetadata(null));

    public static readonly DependencyProperty TextoFecharProperty =
        DependencyProperty.Register(nameof(TextoFechar), typeof(string), typeof(DialogoNaJanela), new PropertyMetadata(string.Empty));

    private TaskCompletionSource<RespostaDoDialogo>? _resposta;
    private RespostaDoDialogo _padrao;
    private IInputElement? _focoAntes;
    private Button? _botaoPrincipal, _botaoSecundario, _botaoFechar;

    static DialogoNaJanela()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogoNaJanela), new FrameworkPropertyMetadata(typeof(DialogoNaJanela)));
        VisibilityProperty.OverrideMetadata(typeof(DialogoNaJanela), new FrameworkPropertyMetadata(Visibility.Collapsed));
    }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }
    public string TextoPrincipal { get => (string)GetValue(TextoPrincipalProperty); set => SetValue(TextoPrincipalProperty, value); }
    public string? TextoSecundario { get => (string?)GetValue(TextoSecundarioProperty); set => SetValue(TextoSecundarioProperty, value); }
    public string TextoFechar { get => (string)GetValue(TextoFecharProperty); set => SetValue(TextoFecharProperty, value); }

    /// <summary>Está esperando uma resposta.</summary>
    public bool Aberto => _resposta != null;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _botaoPrincipal = Ligar("botaoPrincipal", RespostaDoDialogo.Principal);
        _botaoSecundario = Ligar("botaoSecundario", RespostaDoDialogo.Secundaria);
        _botaoFechar = Ligar("botaoFecharDialogo", RespostaDoDialogo.Fechar);
    }

    private Button? Ligar(string nome, RespostaDoDialogo resposta)
    {
        if (GetTemplateChild(nome) is not Button botao)
            return null;
        botao.Click += (_, _) => Responder(resposta);
        return botao;
    }

    /// <summary>
    /// Mostra a pergunta e espera um botão. <paramref name="padrao"/> fica destacado (laranja), com o foco, e é o do
    /// Enter. Sem <paramref name="secundario"/> aparecem só dois botões. Com outra pergunta aberta, responde
    /// <see cref="RespostaDoDialogo.Fechar"/> na hora.
    /// </summary>
    public Task<RespostaDoDialogo> PerguntarAsync(string titulo, string texto, string principal, string? secundario, string fechar,
        RespostaDoDialogo padrao)
    {
        if (_resposta != null)
            return Task.FromResult(RespostaDoDialogo.Fechar);

        Titulo = titulo;
        Texto = texto;
        TextoPrincipal = principal;
        TextoSecundario = secundario;
        TextoFechar = fechar;
        _padrao = padrao;
        _resposta = new TaskCompletionSource<RespostaDoDialogo>();
        _focoAntes = Keyboard.FocusedElement;
        Visibility = Visibility.Visible;

        ApplyTemplate();
        MarcarPadrao();
        // O foco vai para o botão padrão depois de ele aparecer na tela
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => BotaoDe(_padrao)?.Focus());
        return _resposta.Task;
    }

    /// <summary>Fecha o diálogo com a resposta dada (o Esc da janela manda <see cref="RespostaDoDialogo.Fechar"/>).</summary>
    public void Responder(RespostaDoDialogo resposta)
    {
        if (_resposta == null)
            return;

        var pendente = _resposta;
        _resposta = null;
        Visibility = Visibility.Collapsed;
        _focoAntes?.Focus();
        _focoAntes = null;
        pendente.SetResult(resposta);
    }

    // O botão padrão usa o estilo do botão principal; os outros, o comum
    private void MarcarPadrao()
    {
        var principal = (Style)FindResource("BotaoPrincipal");
        foreach (var (botao, resposta) in new[] { (_botaoPrincipal, RespostaDoDialogo.Principal),
                     (_botaoSecundario, RespostaDoDialogo.Secundaria), (_botaoFechar, RespostaDoDialogo.Fechar) })
        {
            if (botao == null)
                continue;
            if (resposta == _padrao)
                botao.Style = principal;
            else
                botao.ClearValue(StyleProperty); // volta o estilo comum dos botões
        }
    }

    private Button? BotaoDe(RespostaDoDialogo resposta) => resposta switch
    {
        RespostaDoDialogo.Principal => _botaoPrincipal,
        RespostaDoDialogo.Secundaria => _botaoSecundario,
        _ => _botaoFechar,
    };

    // Os textos de dentro do modelo ficam escondidos dos leitores de tela: o diálogo se apresenta com o título
    // (nome) e o texto (ajuda)
    protected override AutomationPeer OnCreateAutomationPeer() => new ApresentacaoDoDialogo(this);

    private sealed class ApresentacaoDoDialogo(DialogoNaJanela dialogo) : FrameworkElementAutomationPeer(dialogo)
    {
        protected override string GetNameCore() => dialogo.Titulo;
        protected override string GetHelpTextCore() => dialogo.Texto;
        protected override string GetClassNameCore() => nameof(DialogoNaJanela);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    }
}
