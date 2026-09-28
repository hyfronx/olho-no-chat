using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Testes.Configuracoes;

/// <summary>A gravação meio segundo depois da última mudança (tamanho do texto e fundo da barra laranja).</summary>
public sealed class GravacaoAtrasadaTestes
{
    [Fact]
    public async Task VariosPedidosSeguidos_GravaUmaVezSo()
    {
        int gravacoes = 0;
        var atrasada = new GravacaoAtrasada(() => Interlocked.Increment(ref gravacoes), TimeSpan.FromMilliseconds(200));

        for (int i = 0; i < 10; i++)
        {
            atrasada.Pedir();
            await Task.Delay(20);
        }
        Assert.Equal(0, gravacoes);

        await Task.Delay(500);

        Assert.Equal(1, gravacoes);
    }

    [Fact]
    public async Task Cancelar_NaoGrava()
    {
        int gravacoes = 0;
        var atrasada = new GravacaoAtrasada(() => Interlocked.Increment(ref gravacoes), TimeSpan.FromMilliseconds(100));

        atrasada.Pedir();
        atrasada.Cancelar();
        await Task.Delay(300);

        Assert.Equal(0, gravacoes);
    }

    [Fact]
    public async Task PedidoDepoisDeGravar_GravaDeNovo()
    {
        int gravacoes = 0;
        var atrasada = new GravacaoAtrasada(() => Interlocked.Increment(ref gravacoes), TimeSpan.FromMilliseconds(50));

        atrasada.Pedir();
        await Task.Delay(250);
        atrasada.Pedir();
        await Task.Delay(250);

        Assert.Equal(2, gravacoes);
    }
}
