using OlhoNoChat.Configuracoes;

namespace OlhoNoChat.Twitch;

/// <summary>A conta dentro de Configuracoes.json (objeto "Conta", com o token protegido pelo Windows).</summary>
public sealed class ContaSalvaNasConfiguracoes(ArquivoDeConfiguracoes arquivo) : IContaSalva
{
    // Lida a cada vez: "Restaurar tudo para o padrão" troca o objeto das opções (e mantém a conta)
    private ContaGravada Conta => arquivo.Opcoes.Conta;

    public string Token { get => Conta.Token; set => Conta.Token = value; }
    public string Id { get => Conta.Id; set => Conta.Id = value; }
    public string Login { get => Conta.Login; set => Conta.Login = value; }
    public string NomeDeExibicao { get => Conta.NomeDeExibicao; set => Conta.NomeDeExibicao = value; }

    public void Gravar() => arquivo.Gravar();
}
