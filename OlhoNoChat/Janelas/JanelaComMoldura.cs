#nullable enable
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OlhoNoChat.Atalhos;
using OlhoNoChat.Controles;
using OlhoNoChat.Sistema;

namespace OlhoNoChat.Janelas;

/// <summary>
/// A base das janelas com moldura laranja (Configurações e Filtros do chat): barra de título de 44 px com
/// ícone, nome e "×", coluna de navegação, área das páginas com o canto arredondado, barra de baixo com "Salvar" e
/// "Fechar". "Salvar" grava sem fechar e mostra "✓ Salvo" por 2,5 s; Fechar, Esc, "×" e Alt+F4 perguntam antes de
/// fechar se algo mudou. O visual está em Estilos/Controles.xaml (EstiloJanelaComMoldura).
/// </summary>
public abstract class JanelaComMoldura : Window
{
    public static readonly DependencyProperty IconeProperty =
        DependencyProperty.Register(nameof(Icone), typeof(string), typeof(JanelaComMoldura), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NavegacaoProperty =
        DependencyProperty.Register(nameof(Navegacao), typeof(object), typeof(JanelaComMoldura), new PropertyMetadata(null));

    public static readonly DependencyProperty LarguraDaNavegacaoProperty =
        DependencyProperty.Register(nameof(LarguraDaNavegacao), typeof(double), typeof(JanelaComMoldura), new PropertyMetadata(170.0));

    private readonly DispatcherTimer _tempoDoSalvo = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private DialogoNaJanela? _dialogo;
    private Button? _botaoSalvar;
    private bool _fecharSemPerguntar;

    protected JanelaComMoldura()
    {
        SetResourceReference(StyleProperty, "EstiloJanelaComMoldura");
        _tempoDoSalvo.Tick += (_, _) => MostrarSalvar();
        Loaded += (_, _) => MolduraDaJanela.Aplicar(this);
    }

    /// <summary>O ícone da barra de título (um caractere da fonte de ícones do Windows).</summary>
    public string Icone { get => (string)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }

    /// <summary>O que fica na coluna laranja da esquerda (a lista das páginas).</summary>
    public object? Navegacao { get => GetValue(NavegacaoProperty); set => SetValue(NavegacaoProperty, value); }

    public double LarguraDaNavegacao { get => (double)GetValue(LarguraDaNavegacaoProperty); set => SetValue(LarguraDaNavegacaoProperty, value); }

    /// <summary>O diálogo dentro da janela ("Salvar as alterações?" e outros que a janela quiser mostrar).</summary>
    protected DialogoNaJanela? Dialogo => _dialogo;

    /// <summary>O que está na tela é diferente do que foi salvo.</summary>
    protected abstract bool TemMudancas { get; }

    /// <summary>Grava e aplica o que está na tela.</summary>
    protected abstract void Salvar();

    /// <summary>O texto do "Salvar as alterações?".</summary>
    protected virtual string TextoDaPerguntaAoFechar => "Você mudou algumas opções e ainda não salvou.";

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dialogo = GetTemplateChild("dialogo") as DialogoNaJanela;
        _botaoSalvar = GetTemplateChild("botaoSalvar") as Button;
        if (_botaoSalvar != null)
            _botaoSalvar.Click += (_, _) => SalvarEMostrar();
        if (GetTemplateChild("botaoFechar") is Button fechar)
            fechar.Click += (_, _) => Close();
        if (GetTemplateChild("botaoFecharJanela") is Button fecharJanela)
            fecharJanela.Click += (_, _) => Close();
    }

    private void SalvarEMostrar()
    {
        Salvar();
        MostrarSalvo();
    }

    /// <summary>"✓ Salvo" no botão por um momento; salvar de novo recomeça a contagem.</summary>
    protected void MostrarSalvo()
    {
        if (_botaoSalvar == null)
            return;

        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        conteudo.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = (FontFamily)FindResource("FonteDosIcones"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        conteudo.Children.Add(new TextBlock { Text = "Salvo", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _botaoSalvar.Content = conteudo;
        AutomationProperties.SetName(_botaoSalvar, "Salvo");

        _tempoDoSalvo.Stop();
        _tempoDoSalvo.Start();
    }

    private void MostrarSalvar()
    {
        _tempoDoSalvo.Stop();
        if (_botaoSalvar == null)
            return;
        _botaoSalvar.Content = "Salvar";
        AutomationProperties.SetName(_botaoSalvar, "Salvar");
    }

    // O Esc chega aqui antes do controle com o foco (alguns o engoliriam). Com um diálogo aberto ele fecha só o
    // diálogo; numa lista aberta ou numa caixa de atalho gravando ele é do controle.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || e.Key != Key.Escape)
            return;

        if (_dialogo?.Aberto == true)
        {
            _dialogo.Responder(RespostaDoDialogo.Fechar);
            e.Handled = true;
        }
        else if (!EscPertenceAoControleComFoco())
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>O Esc fecha uma lista aberta ou apaga uma caixa de atalho que está gravando.</summary>
    public static bool EscPertenceAoControleComFoco()
    {
        var elemento = Keyboard.FocusedElement as DependencyObject;
        while (elemento != null)
        {
            switch (elemento)
            {
                case ComboBoxItem:
                case ComboBox { IsDropDownOpen: true }:
                case EditorDeAtalho { Gravando: true }:
                    return true;
            }

            // As listas mostram os itens numa janelinha à parte, fora da árvore visual da janela
            elemento = (elemento is Visual ? VisualTreeHelper.GetParent(elemento) : null) ?? LogicalTreeHelper.GetParent(elemento);
        }
        return false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _fecharSemPerguntar)
            return;

        // Alt+F4 ou "×" com a pergunta já aberta: ela continua esperando a resposta
        if (_dialogo?.Aberto == true)
        {
            e.Cancel = true;
            return;
        }

        if (!TemMudancas || _dialogo == null)
            return;

        // Pergunta antes, para nada se perder sem querer
        e.Cancel = true;
        Dispatcher.BeginInvoke(PerguntarAntesDeFechar);
    }

    /// <summary>
    /// Mostra uma pergunta no diálogo da janela (ver <see cref="DialogoNaJanela.PerguntarAsync"/>). Enquanto ela está
    /// aberta, o Enter é do diálogo, não do "Salvar".
    /// </summary>
    protected async Task<RespostaDoDialogo> PerguntarAsync(string titulo, string texto, string principal, string? secundario,
        string fechar, RespostaDoDialogo padrao)
    {
        if (_dialogo == null)
            return RespostaDoDialogo.Fechar;

        bool padraoDaJanela = _botaoSalvar?.IsDefault == true;
        if (_botaoSalvar != null)
            _botaoSalvar.IsDefault = false;

        RespostaDoDialogo resposta = await _dialogo.PerguntarAsync(titulo, texto, principal, secundario, fechar, padrao);

        if (_botaoSalvar != null)
            _botaoSalvar.IsDefault = padraoDaJanela;
        return resposta;
    }

    private async void PerguntarAntesDeFechar()
    {
        RespostaDoDialogo resposta = await PerguntarAsync("Salvar as alterações?", TextoDaPerguntaAoFechar,
            "Salvar", "Não salvar", "Voltar", RespostaDoDialogo.Principal);

        if (resposta == RespostaDoDialogo.Principal)
            SalvarEMostrar();
        else if (resposta != RespostaDoDialogo.Secundaria)
            return; // "Voltar": continua editando

        _fecharSemPerguntar = true;
        Close();
    }
}
