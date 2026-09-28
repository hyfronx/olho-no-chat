#nullable enable
namespace OlhoNoChat.Configuracoes;

/// <summary>
/// Grava um pouco depois da última mudança: o controle deslizante manda dezenas de mudanças enquanto é arrastado,
/// e gravar só no fim do app perderia a mudança se ele não fechasse direito.
/// </summary>
/// <remarks>
/// A gravação roda no mesmo lugar em que o pedido foi feito (na tela, a linha de execução da janela), para nunca
/// ler as opções ao mesmo tempo em que a tela as muda.
/// </remarks>
public sealed class GravacaoAtrasada
{
    private readonly Action _gravar;
    private readonly TimeSpan _espera;
    private readonly object _trava = new();
    private CancellationTokenSource? _pendente;

    public GravacaoAtrasada(Action gravar, TimeSpan espera)
    {
        _gravar = gravar;
        _espera = espera;
    }

    /// <summary>Grava depois da espera; um pedido novo antes disso recomeça a espera.</summary>
    public void Pedir()
    {
        var novo = new CancellationTokenSource();
        lock (_trava)
        {
            _pendente?.Cancel();
            _pendente = novo;
        }
        _ = EsperarEGravarAsync(novo);
    }

    /// <summary>Esquece o pedido pendente (quem chama vai gravar agora).</summary>
    public void Cancelar()
    {
        lock (_trava)
        {
            _pendente?.Cancel();
            _pendente = null;
        }
    }

    private async Task EsperarEGravarAsync(CancellationTokenSource pedido)
    {
        try
        {
            await Task.Delay(_espera, pedido.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_trava)
        {
            if (_pendente != pedido)
                return;
            _pendente = null;
        }
        _gravar();
    }
}
