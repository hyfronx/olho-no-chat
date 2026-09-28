namespace OlhoNoChat.Janelas.Filtros;

/// <summary>O que o chat faz com a lista de usuários (página Usuários).</summary>
public enum ModoDaLista
{
    /// <summary>A lista fica guardada, mas todo mundo aparece do mesmo jeito.</summary>
    Nada,

    /// <summary>Todo mundo aparece, e as pessoas da lista ficam com um fundo colorido.</summary>
    Destacar,

    /// <summary>Só as mensagens das pessoas da lista aparecem.</summary>
    SoALista,
}
