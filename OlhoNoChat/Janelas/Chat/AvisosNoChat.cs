using System.Text.Json;
using OlhoNoChat.Atalhos;

namespace OlhoNoChat.Janelas.Chat;

/// <summary>
/// Os avisos que aparecem por cima da página do chat (qualquer página, também a de boas-vindas): uma caixa no topo que
/// não recebe cliques, some depois de 10 s e é trocada por um aviso novo. Os que precisam esperar a página carregar
/// ficam guardados: o de bordas ocultas, o de que dá para escrever (também espera as Configurações fecharem) e o do
/// arquivo de configurações ilegível (decisão 20).
/// </summary>
public sealed class AvisosNoChat
{
    private readonly Func<bool> _paginaPronta;
    private readonly Action<string> _executar;
    private bool _bordasOcultasEsperando;
    private bool _daParaEscreverEsperando;
    private string? _avisoDasConfiguracoes;

    /// <param name="paginaPronta">Uma página carregou e o navegador está funcionando.</param>
    /// <param name="executar">Roda um script na página aberta.</param>
    /// <param name="avisoDasConfiguracoes">O aviso do arquivo de configurações ilegível, mostrado uma vez (ou null).</param>
    public AvisosNoChat(Func<bool> paginaPronta, Action<string> executar, string? avisoDasConfiguracoes)
    {
        _paginaPronta = paginaPronta;
        _executar = executar;
        _avisoDasConfiguracoes = avisoDasConfiguracoes;
    }

    /// <summary>A caixa do aviso: 8 px do topo e das laterais, 17 px em negrito, 10 s na tela e 0,6 s para sumir.</summary>
    public static string ScriptParaMostrar(string texto) => $$"""
        (function (texto) {
            var antigo = document.getElementById('onc-toast');
            if (antigo) antigo.remove();
            var aviso = document.createElement('div');
            aviso.id = 'onc-toast';
            aviso.textContent = texto;
            aviso.style.cssText = 'position:fixed;top:8px;left:8px;right:8px;z-index:2147483647;' +
                'background:#141414;color:#fff;border:2px solid #FF8A65;border-radius:8px;padding:10px 14px;' +
                'font:700 17px "Segoe UI",Arial,sans-serif;line-height:1.35;white-space:pre-line;' +
                'text-shadow:none;letter-spacing:0;pointer-events:none;transition:opacity .6s;';
            document.body.appendChild(aviso);
            setTimeout(function () { aviso.style.opacity = '0'; }, 10000);
            setTimeout(function () { aviso.remove(); }, 10700);
        })({{JsonSerializer.Serialize(texto)}});
        """;

    public const string ScriptParaEsconder = "(function () { var aviso = document.getElementById('onc-toast'); if (aviso) aviso.remove(); })();";

    /// <summary>Como mostrar as bordas de novo.</summary>
    public static string TextoBordasOcultas(Atalho? atalhoDasBordas) =>
        "Bordas ocultas: agora só o chat fica por cima do jogo.\n" + (Atalho.Existe(atalhoDasBordas)
            ? $"Para mostrar de novo: aperte {atalhoDasBordas}, ou clique com o botão direito no ícone do Olho no Chat perto do relógio."
            : "Para mostrar de novo: clique com o botão direito no ícone do Olho no Chat perto do relógio.");

    /// <summary>Mostra agora (se a página estiver pronta; senão o aviso se perde, como antes).</summary>
    public void Mostrar(string texto)
    {
        if (_paginaPronta())
            _executar(ScriptParaMostrar(texto));
    }

    /// <summary>As bordas foram ocultas: o aviso aparece quando a página estiver pronta.</summary>
    public void PedirBordasOcultas() => _bordasOcultasEsperando = true;

    /// <summary>As bordas voltaram: o aviso some na hora e o que estava esperando é esquecido.</summary>
    public void EsconderBordasOcultas()
    {
        _bordasOcultasEsperando = false;
        _executar(ScriptParaEsconder);
    }

    /// <summary>O tipo de chat passou a ser de canal nas Configurações: aparece quando elas fecharem e a página carregar.</summary>
    public void PedirDaParaEscrever() => _daParaEscreverEsperando = true;

    /// <summary>
    /// Mostra os avisos que estão esperando, se a página já está pronta. O mais importante (o das configurações) por
    /// último, para ficar na tela.
    /// </summary>
    /// <param name="bordasOcultas">O de bordas ocultas só aparece se elas ainda estiverem ocultas.</param>
    /// <param name="configuracoesAbertas">O de escrever espera as Configurações fecharem.</param>
    /// <param name="atalhoDasBordas">Para o texto do aviso de bordas ocultas.</param>
    /// <param name="textoDaParaEscrever">O texto do aviso de escrever na hora de mostrar (null = não mostra).</param>
    public void MostrarOsQueEsperam(bool bordasOcultas, bool configuracoesAbertas, Atalho? atalhoDasBordas,
        Func<string?> textoDaParaEscrever)
    {
        if (!_paginaPronta())
            return;

        if (_bordasOcultasEsperando && bordasOcultas)
        {
            _bordasOcultasEsperando = false;
            Mostrar(TextoBordasOcultas(atalhoDasBordas));
        }

        if (_daParaEscreverEsperando && !configuracoesAbertas)
        {
            _daParaEscreverEsperando = false;
            string? texto = textoDaParaEscrever();
            if (texto != null)
                Mostrar(texto);
        }

        if (_avisoDasConfiguracoes != null)
        {
            Mostrar(_avisoDasConfiguracoes);
            _avisoDasConfiguracoes = null;
        }
    }
}
