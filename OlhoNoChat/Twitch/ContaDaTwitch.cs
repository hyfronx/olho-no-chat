using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>
/// A conta da Twitch conectada na aba Twitch. O acesso vem do navegador da própria pessoa ("Conectar", ver
/// <see cref="AutorizacaoNoNavegador"/>); aqui ele é conferido com a Twitch, guardado e esquecido.
/// </summary>
public sealed class ContaDaTwitch
{
    private readonly ApiDaTwitch _api;
    private readonly IContaSalva _salva;
    private readonly ILogger<ContaDaTwitch> _log;
    private Task<bool>? _verificacaoDaExecucao;

    public ContaDaTwitch(ApiDaTwitch api, IContaSalva salva, ILogger<ContaDaTwitch> log)
    {
        _api = api;
        _salva = salva;
        _log = log;
    }

    /// <summary>A conta foi conectada, conferida ou esquecida (na thread de quem pediu a mudança).</summary>
    public event Action? Mudou;

    public bool EstaConectada => _salva.Token.Length > 0 && _salva.Id.Length > 0;

    public string Token => _salva.Token;
    public string Id => _salva.Id;
    public string Login => _salva.Login;

    /// <summary>O nome de exibição, ou o login se ele estiver vazio.</summary>
    public string NomeMostrado => _salva.NomeDeExibicao.Length > 0 ? _salva.NomeDeExibicao : _salva.Login;

    /// <summary>Endereço da foto da conta (só na memória, depois da verificação).</summary>
    public string Foto { get; private set; } = string.Empty;

    // Antes da primeira verificação da execução, supõe que pode enviar e não pode ler emotes
    public bool PodeEnviar { get; private set; } = true;

    /// <summary>A lista de emotes precisa de "user:read:emotes", que acessos de antes da 1.0.18 não têm.</summary>
    public bool PodeLerEmotes { get; private set; }

    /// <summary>As permissões acima foram lidas da Twitch nesta execução.</summary>
    public bool PermissoesConferidas { get; private set; }

    /// <summary>
    /// A verificação da execução: a primeira chamada confere o acesso salvo, as seguintes reaproveitam o resultado.
    /// </summary>
    public Task<bool> VerificarUmaVezAsync() => _verificacaoDaExecucao ??= VerificarAsync();

    /// <summary>
    /// Confere o acesso salvo com a Twitch. Um acesso expirado, removido na twitch.tv ou de outro aplicativo é
    /// esquecido. Devolve se a conta continua conectada (sem internet ela é mantida).
    /// </summary>
    public Task<bool> VerificarAsync() => VerificarAsync(tokenNovo: false);

    /// <summary>Uma chamada à Twitch respondeu 401: o acesso é conferido de novo, sem esperar.</summary>
    public void AcessoRecusado() => _ = VerificarAsync();

    /// <summary>Guarda um acesso novo vindo do navegador, se a Twitch o confirmar.</summary>
    public async Task<bool> ConectarAsync(string token)
    {
        string anterior = _salva.Token;
        _salva.Token = token;
        bool confirmado = await VerificarAsync(tokenNovo: anterior != token);
        if (!confirmado)
            Esquecer();
        else if (anterior.Length > 0 && anterior != token)
            RevogarEmSegundoPlano(anterior); // conectou de novo (por exemplo, por uma permissão nova)
        return confirmado;
    }

    /// <summary>Esquece a conta na hora e cancela o acesso na Twitch em segundo plano.</summary>
    public void Desconectar()
    {
        string token = _salva.Token;
        Esquecer();
        if (token.Length > 0)
            RevogarEmSegundoPlano(token);
    }

    /// <summary>Se o canal existe na Twitch; null quando não deu para saber (sem conta ou sem internet).</summary>
    public async Task<bool?> CanalExisteAsync(string login)
    {
        if (_salva.Token.Length == 0)
            return null;
        try
        {
            return await _api.IdDoCanalAsync(_salva.Token, login) != null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Não deu para saber se o canal existe.");
            return null;
        }
    }

    // O arquivo só é gravado, e a mudança avisada, quando algo mudou (a verificação roda a cada Configurações aberta)
    private async Task<bool> VerificarAsync(bool tokenNovo)
    {
        string token = _salva.Token;
        if (token.Length == 0)
            return false;

        ApiDaTwitch.Resposta validacao;
        try
        {
            validacao = await _api.ValidarAsync(token);
        }
        catch (Exception ex)
        {
            // Sem internet: a conta fica (numa primeira conexão ainda não há id, então ela não conta como conectada)
            _log.LogWarning(ex, "Não deu para conferir o acesso da Twitch.");
            return EstaConectada;
        }

        if (validacao.Codigo == HttpStatusCode.Unauthorized)
        {
            _log.LogInformation("O acesso da Twitch não vale mais.");
            Esquecer();
            return false;
        }
        JsonNode? info = validacao.Deu ? validacao.Json : null;
        if (info == null)
        {
            _log.LogWarning("A validação do acesso respondeu {Codigo}.", (int)validacao.Codigo);
            return EstaConectada;
        }
        if ((string?)info["client_id"] != InfoDoApp.TwitchClientId)
        {
            _log.LogInformation("O acesso salvo é de outro aplicativo da Twitch: esquecido.");
            Esquecer();
            return false;
        }

        var salvoAntes = (_salva.Id, _salva.Login, _salva.NomeDeExibicao);
        var estadoAntes = (Foto, PodeEnviar, PodeLerEmotes, PermissoesConferidas);

        _salva.Id = (string?)info["user_id"] ?? string.Empty;
        _salva.Login = (string?)info["login"] ?? string.Empty;
        var permissoes = (info["scopes"] as JsonArray)?.Select(p => (string?)p).ToHashSet() ?? [];
        PodeEnviar = permissoes.Contains("user:write:chat");
        PodeLerEmotes = permissoes.Contains("user:read:emotes");
        PermissoesConferidas = true;

        await BuscarPerfilAsync(token);

        bool salvoMudou = tokenNovo || salvoAntes != (_salva.Id, _salva.Login, _salva.NomeDeExibicao);
        if (salvoMudou)
            _salva.Gravar();
        if (salvoMudou || estadoAntes != (Foto, PodeEnviar, PodeLerEmotes, PermissoesConferidas))
            Mudou?.Invoke();
        return true;
    }

    // Nome de exibição e foto; uma falha aqui não muda nada
    private async Task BuscarPerfilAsync(string token)
    {
        try
        {
            ApiDaTwitch.Resposta resposta = await _api.UsuariosAsync(token);
            JsonNode? usuario = resposta.Deu ? resposta.Dados.FirstOrDefault() : null;
            if (usuario == null)
                return;
            _salva.NomeDeExibicao = (string?)usuario["display_name"] ?? string.Empty;
            Foto = (string?)usuario["profile_image_url"] ?? string.Empty;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Não deu para buscar o perfil da conta.");
        }
    }

    private void Esquecer()
    {
        _salva.Token = string.Empty;
        _salva.Id = string.Empty;
        _salva.Login = string.Empty;
        _salva.NomeDeExibicao = string.Empty;
        Foto = string.Empty;
        PodeLerEmotes = false;
        PermissoesConferidas = false;
        _salva.Gravar();
        Mudou?.Invoke();
    }

    private void RevogarEmSegundoPlano(string token) => _ = Task.Run(async () =>
    {
        try
        {
            await _api.RevogarAsync(token);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Não deu para cancelar o acesso na Twitch.");
        }
    });
}
