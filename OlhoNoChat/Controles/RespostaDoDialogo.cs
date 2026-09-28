namespace OlhoNoChat.Controles;

/// <summary>O botão escolhido num <see cref="DialogoNaJanela"/>.</summary>
public enum RespostaDoDialogo
{
    /// <summary>"Salvar", "Restaurar"...</summary>
    Principal,

    /// <summary>"Não salvar".</summary>
    Secundaria,

    /// <summary>"Voltar", "Cancelar" ou Esc.</summary>
    Fechar,
}
