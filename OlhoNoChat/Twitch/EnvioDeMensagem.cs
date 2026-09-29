using System.Net;
using Microsoft.Extensions.Logging;

namespace OlhoNoChat.Twitch;

/// <summary>Envia mensagens da caixa "Escrever no chat…" como a conta conectada, com o texto de cada erro.</summary>
public sealed class EnvioDeMensagem
{
    public enum Situacao { Enviada, NaoPublicada, SemConta, Falhou }

    /// <param name="Texto">O que aparece embaixo da caixa ("" quando foi enviada).</param>
    public sealed record Resultado(Situacao Situacao, string Texto = "");

    private readonly ApiDaTwitch _api;
    private readonly ContaDaTwitch _conta;
    private readonly ILogger<EnvioDeMensagem> _log;

    public EnvioDeMensagem(ApiDaTwitch api, ContaDaTwitch conta, ILogger<EnvioDeMensagem> log)
    {
        _api = api;
        _conta = conta;
        _log = log;
    }

    public async Task<Resultado> EnviarAsync(string canal, string texto)
    {
        if (!_conta.EstaConectada)
            return new Resultado(Situacao.SemConta, "Conecte sua conta da Twitch nas Configurações (aba Twitch).");

        try
        {
            string? idDoCanal = await _api.IdDoCanalAsync(_conta.Token, canal);
            if (idDoCanal == null)
                return new Resultado(Situacao.Falhou, $"Não achei o canal \"{canal}\" na Twitch.");

            ApiDaTwitch.Resposta resposta = await _api.EnviarMensagemAsync(_conta.Token, idDoCanal, _conta.Id, texto);
            if (resposta.Codigo == HttpStatusCode.Unauthorized)
                _conta.AcessoRecusado();
            else if (!resposta.Deu)
                _log.LogWarning("A Twitch recusou a mensagem: {Codigo} {Corpo}", (int)resposta.Codigo, resposta.Corpo);
            return LerResposta(resposta);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A mensagem não foi enviada.");
            return new Resultado(Situacao.Falhou, "Não foi possível enviar. Confira sua internet.");
        }
    }

    /// <summary>O que a resposta do envio quer dizer para a pessoa.</summary>
    public static Resultado LerResposta(ApiDaTwitch.Resposta resposta)
    {
        switch (resposta.Codigo)
        {
            case HttpStatusCode.OK:
                var dados = resposta.Dados.FirstOrDefault();
                if ((bool?)dados?["is_sent"] == true)
                    return new Resultado(Situacao.Enviada);
                string? motivo = (string?)dados?["drop_reason"]?["message"];
                return new Resultado(Situacao.NaoPublicada, string.IsNullOrWhiteSpace(motivo)
                    ? "A Twitch não publicou a mensagem."
                    : $"A Twitch não publicou a mensagem: {motivo}");

            case HttpStatusCode.Unauthorized:
                return new Resultado(Situacao.SemConta, "A conexão com a Twitch expirou. Conecte de novo nas Configurações (aba Twitch).");

            case HttpStatusCode.Forbidden:
                return new Resultado(Situacao.NaoPublicada, "A Twitch não deixou enviar nesse chat (você pode estar banido ou suspenso nele).");

            case HttpStatusCode.TooManyRequests:
                return new Resultado(Situacao.NaoPublicada, "Muitas mensagens seguidas. Espere um pouco e tente de novo.");

            default:
                string? erro = (string?)resposta.Json?["message"];
                return new Resultado(Situacao.Falhou, string.IsNullOrEmpty(erro)
                    ? $"Não foi possível enviar ({(int)resposta.Codigo})."
                    : $"Não foi possível enviar ({(int)resposta.Codigo}: {erro}).");
        }
    }
}
