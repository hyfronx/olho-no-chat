#nullable enable
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OlhoNoChat.Configuracoes;

/// <summary>
/// O arquivo Configuracoes.json da pasta de dados: ler, gravar (sem nunca deixar o arquivo pela metade), converter os
/// arquivos das versões antigas e "Restaurar tudo para o padrão".
/// </summary>
/// <remarks>
/// <see cref="Abrir"/> só lê: uma segunda cópia do app que vai fechar logo não pode mexer em nada. A cópia principal
/// chama <see cref="ComecarAGravar"/>, que termina o que ficou pendente na abertura (conversão, cópia do arquivo
/// ilegível) e libera as gravações.
/// </remarks>
public sealed class ArquivoDeConfiguracoes
{
    public const string NomeDoArquivo = "Configuracoes.json";
    public const string PastaDosAntigos = "Configurações antigas (antes da 2.0.0)";
    public const string PastaComProblema = "Configurações com problema";
    public const string PastaAntesDeRestaurar = "Configurações antes de restaurar o padrão";

    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    public static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        // Acentos e símbolos do CSS legíveis no arquivo (ele só é lido pelo app)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new ConversorDeCor() },
    };

    private readonly GravacaoAtrasada _atrasada;
    private ImportacaoDoArquivoAntigo.Resultado? _conversaoPendente;
    private bool _antigosSobrando;
    private string? _ilegivelParaGuardar;

    private ArquivoDeConfiguracoes(string pasta)
    {
        Pasta = pasta;
        _atrasada = new GravacaoAtrasada(() => Gravar(), TimeSpan.FromMilliseconds(500));
    }

    public string Pasta { get; }
    public string Caminho => Path.Combine(Pasta, NomeDoArquivo);

    /// <summary>As opções em uso. "Restaurar tudo para o padrão" troca o objeto inteiro.</summary>
    public Opcoes Opcoes { get; private set; } = new();

    /// <summary>Texto para mostrar no chat quando o arquivo não pôde ser lido (null = tudo certo).</summary>
    public string? Aviso { get; private set; }

    /// <summary>Só a cópia principal grava (depois de <see cref="ComecarAGravar"/>).</summary>
    public bool PodeGravar { get; private set; }

    /// <summary>Lê as opções da pasta, sem gravar nada.</summary>
    public static ArquivoDeConfiguracoes Abrir(string pasta)
    {
        var arquivo = new ArquivoDeConfiguracoes(pasta);
        arquivo.Ler();
        return arquivo;
    }

    private void Ler()
    {
        if (File.Exists(Caminho))
        {
            string texto = File.ReadAllText(Caminho);
            try
            {
                Opcoes = DeJson(texto);
                _antigosSobrando = ArquivosAntigos(todosOsJson: false).Any(); // de uma conversão que não conseguiu mover
                return;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Configuracoes.json ilegível: {ex.Message}");
                _ilegivelParaGuardar = Path.Combine(Pasta, PastaComProblema, $"Configuracoes {DateTime.Now:yyyy-MM-dd HH-mm-ss}.json");
                Aviso = AvisoDoArquivoIlegivel(_ilegivelParaGuardar);
                return;
            }
        }

        var importacao = ImportacaoDoArquivoAntigo.Ler(Pasta);
        if (importacao == null)
            return; // primeira instalação: os padrões

        Opcoes = importacao.Opcoes;
        _conversaoPendente = importacao;
        if (importacao.OpcoesIlegiveis)
            Aviso = AvisoDoArquivoIlegivel(NomeLivre(Path.Combine(Pasta, PastaDosAntigos), ImportacaoDoArquivoAntigo.ArquivoDasOpcoes));
    }

    private static string AvisoDoArquivoIlegivel(string copia) =>
        "Não deu para ler o arquivo de configurações, então o Olho no Chat abriu com as opções da primeira instalação.\n" +
        $"Uma cópia do arquivo ficou em: {copia}";

    /// <summary>
    /// Na cópia principal: grava o arquivo novo convertido e guarda os antigos, ou guarda a cópia do arquivo ilegível,
    /// e daí em diante grava normalmente.
    /// </summary>
    public void ComecarAGravar()
    {
        PodeGravar = true;

        if (_ilegivelParaGuardar != null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_ilegivelParaGuardar)!);
                File.Copy(Caminho, _ilegivelParaGuardar, overwrite: false);
                Gravar();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sem a cópia, o arquivo ilegível não é substituído: fica para alguém olhar
                Debug.WriteLine($"Não deu para guardar a cópia do arquivo ilegível: {ex.Message}");
                PodeGravar = false;
            }
            _ilegivelParaGuardar = null;
        }
        else if (_conversaoPendente != null)
        {
            TerminarConversao(_conversaoPendente);
            _conversaoPendente = null;
        }
        else if (_antigosSobrando)
        {
            GuardarAntigos(todosOsJson: false);
        }
    }

    // Os antigos só saem da pasta depois de o novo estar gravado e lido de volta igual. Se algo falhar, eles ficam
    // onde estão e a conversão é feita de novo na próxima abertura.
    private void TerminarConversao(ImportacaoDoArquivoAntigo.Resultado importacao)
    {
        string texto = ParaJson(Opcoes);
        if (!GravarTexto(texto))
            return;

        try
        {
            string lido = File.ReadAllText(Caminho);
            if (lido != texto || DeJson(lido).Conta.Token != Opcoes.Conta.Token)
            {
                Debug.WriteLine("O arquivo novo não ficou igual ao gravado: a conversão fica para a próxima abertura.");
                File.Delete(Caminho);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Não deu para conferir o arquivo novo: {ex.Message}");
            return;
        }

        GuardarAntigos(todosOsJson: importacao.AntesDa1017);
    }

    private IEnumerable<string> ArquivosAntigos(bool todosOsJson)
    {
        if (todosOsJson)
            return Directory.GetFiles(Pasta, "*.json").Where(a => !Path.GetFileName(a).Equals(NomeDoArquivo, StringComparison.OrdinalIgnoreCase));
        return new[] { ImportacaoDoArquivoAntigo.ArquivoDasOpcoes, ImportacaoDoArquivoAntigo.ArquivoDaJanela }
            .Select(nome => Path.Combine(Pasta, nome))
            .Where(File.Exists);
    }

    // Movidos, nunca apagados. Um nome que já existe na subpasta ganha " (2)", " (3)"...
    private void GuardarAntigos(bool todosOsJson)
    {
        string destino = Path.Combine(Pasta, PastaDosAntigos);
        foreach (string antigo in ArquivosAntigos(todosOsJson).ToList())
        {
            try
            {
                Directory.CreateDirectory(destino);
                File.Move(antigo, NomeLivre(destino, Path.GetFileName(antigo)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Não deu para guardar {antigo}: {ex.Message}");
            }
        }
    }

    private static string NomeLivre(string pasta, string nome)
    {
        string caminho = Path.Combine(pasta, nome);
        for (int n = 2; File.Exists(caminho); n++)
            caminho = Path.Combine(pasta, $"{Path.GetFileNameWithoutExtension(nome)} ({n}){Path.GetExtension(nome)}");
        return caminho;
    }

    /// <summary>Grava agora (e esquece uma gravação atrasada pendente). false = não gravou.</summary>
    public bool Gravar()
    {
        _atrasada.Cancelar();
        return PodeGravar && GravarTexto(ParaJson(Opcoes));
    }

    /// <summary>Grava meio segundo depois da última mudança (tamanho do texto e fundo da barra laranja).</summary>
    public void GravarDaquiAPouco()
    {
        if (PodeGravar)
            _atrasada.Pedir();
    }

    // Grava num arquivo temporário e só depois troca pelo de verdade: uma queda no meio deixa o arquivo anterior inteiro
    private bool GravarTexto(string texto)
    {
        string temporario = $"{Caminho}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Pasta);
            using (var arquivo = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                arquivo.Write(Utf8SemBom.GetBytes(texto));
                arquivo.Flush(flushToDisk: true);
            }
            File.Move(temporario, Caminho, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Não deu para gravar as configurações: {ex.Message}");
            try { File.Delete(temporario); } catch (Exception) { }
            return false;
        }
    }

    /// <summary>
    /// "Restaurar tudo para o padrão": guarda uma cópia do arquivo (substitui a cópia anterior), volta as opções para
    /// as da primeira instalação (menos canal, conta, listas dos filtros e posição da janela) e grava.
    /// </summary>
    public void RestaurarPadrao()
    {
        try
        {
            if (File.Exists(Caminho))
            {
                string copias = Path.Combine(Pasta, PastaAntesDeRestaurar);
                Directory.CreateDirectory(copias);
                File.Copy(Caminho, Path.Combine(copias, NomeDoArquivo), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Não deu para guardar a cópia antes de restaurar: {ex.Message}");
        }

        Opcoes = Opcoes.RestauradasParaOPadrao();
        Gravar();
    }

    public static string ParaJson(Opcoes opcoes) => JsonSerializer.Serialize(opcoes, Formato);

    /// <summary>As opções de um texto do arquivo novo. Lança <see cref="JsonException"/> se não der para ler.</summary>
    public static Opcoes DeJson(string texto)
    {
        Opcoes opcoes;
        try
        {
            opcoes = JsonSerializer.Deserialize<Opcoes>(texto, Formato) ?? throw new JsonException("O arquivo está vazio (null).");
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new JsonException(ex.Message, ex);
        }
        opcoes.Corrigir();
        return opcoes;
    }
}
