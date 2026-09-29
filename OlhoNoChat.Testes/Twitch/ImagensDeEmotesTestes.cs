using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OlhoNoChat.Twitch;

namespace OlhoNoChat.Testes.Twitch;

public sealed class ImagensDeEmotesTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "OlhoNoChat.Testes", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    // A "internet" das imagens: devolve os bytes do endereço pedido (ou 404) e conta os pedidos
    private sealed class CdnFalsa : HttpMessageHandler
    {
        public readonly List<string> Pedidos = [];
        public HttpStatusCode Codigo = HttpStatusCode.OK;
        public TaskCompletionSource? Segurar;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelar)
        {
            lock (Pedidos)
                Pedidos.Add(pedido.RequestUri!.ToString());
            if (Segurar != null)
                await Segurar.Task;
            await Task.Yield();
            return new HttpResponseMessage(Codigo) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(pedido.RequestUri!.AbsolutePath)) };
        }
    }

    private ImagensDeEmotes Criar(CdnFalsa cdn) => new(_pasta, NullLogger<ImagensDeEmotes>.Instance, cdn);

    [Fact]
    public async Task Obter_BaixaUmaVezEDepoisLeDoDisco()
    {
        var cdn = new CdnFalsa();
        byte[]? primeira = await Criar(cdn).ObterAsync("emotesv2_abc", animado: false, "2.0");

        Assert.Equal("/emoticons/v2/emotesv2_abc/static/dark/2.0", System.Text.Encoding.UTF8.GetString(primeira!));
        Assert.True(File.Exists(Path.Combine(_pasta, "emotesv2_abc_parado_2.0.png")));

        // Outra execução do app: vem do disco, sem internet
        var semInternet = new CdnFalsa { Codigo = HttpStatusCode.InternalServerError };
        byte[]? segunda = await Criar(semInternet).ObterAsync("emotesv2_abc", animado: false, "2.0");
        Assert.Equal(primeira, segunda);
        Assert.Empty(semInternet.Pedidos);
        Assert.Single(cdn.Pedidos);
    }

    [Fact]
    public async Task Obter_AnimadoEParadoSaoArquivosDiferentes()
    {
        var cdn = new CdnFalsa();
        var imagens = Criar(cdn);
        await imagens.ObterAsync("25", animado: true, "1.0");
        await imagens.ObterAsync("25", animado: false, "1.0");

        Assert.Equal(["https://static-cdn.jtvnw.net/emoticons/v2/25/animated/dark/1.0", "https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/1.0"],
            cdn.Pedidos);
        Assert.True(File.Exists(Path.Combine(_pasta, "25_animado_1.0.gif")));
        Assert.True(File.Exists(Path.Combine(_pasta, "25_parado_1.0.png")));
    }

    [Fact]
    public async Task Obter_ErroNaoGuardaNadaEDevolveNull()
    {
        var cdn = new CdnFalsa { Codigo = HttpStatusCode.NotFound };
        Assert.Null(await Criar(cdn).ObterAsync("25", animado: true, "1.0"));
        Assert.False(Directory.Exists(_pasta) && Directory.EnumerateFiles(_pasta).Any());
    }

    [Theory]
    [InlineData("../fora", "1.0")]
    [InlineData("25", "1.0/../x")]
    [InlineData("", "1.0")]
    public async Task Obter_IdOuEscalaEstranhosNemSaoPedidos(string id, string escala)
    {
        var cdn = new CdnFalsa();
        Assert.Null(await Criar(cdn).ObterAsync(id, animado: false, escala));
        Assert.Empty(cdn.Pedidos);
    }

    [Fact]
    public async Task Obter_AMesmaImagemAoMesmoTempoEBaixadaUmaVez()
    {
        var cdn = new CdnFalsa { Segurar = new TaskCompletionSource() };
        var imagens = Criar(cdn);
        Task<byte[]?> a = imagens.ObterAsync("25", animado: false, "1.0");
        Task<byte[]?> b = imagens.ObterAsync("25", animado: false, "1.0");
        cdn.Segurar.SetResult();

        Assert.Equal(await a, await b);
        Assert.Single(cdn.Pedidos);
    }

    [Fact]
    public async Task Obter_ApagaAsImagensSemUsoHaMuitoTempo()
    {
        Directory.CreateDirectory(_pasta);
        string velha = Path.Combine(_pasta, "1_parado_1.0.png");
        string recente = Path.Combine(_pasta, "2_parado_1.0.png");
        string lista = Path.Combine(_pasta, ListaDeEmotes.ArquivoDaUltimaLista);
        foreach (string arquivo in new[] { velha, recente, lista })
            File.WriteAllText(arquivo, "x");
        File.SetLastWriteTimeUtc(velha, DateTime.UtcNow.AddDays(-(ImagensDeEmotes.DiasSemUso + 1)));
        File.SetLastWriteTimeUtc(lista, DateTime.UtcNow.AddDays(-(ImagensDeEmotes.DiasSemUso + 1)));

        await Criar(new CdnFalsa()).ObterAsync("3", animado: false, "1.0");
        for (int i = 0; i < 50 && File.Exists(velha); i++)
            await Task.Delay(20); // a limpeza roda por trás

        Assert.False(File.Exists(velha));
        Assert.True(File.Exists(recente));
        Assert.True(File.Exists(lista)); // a lista não é imagem
    }
}
