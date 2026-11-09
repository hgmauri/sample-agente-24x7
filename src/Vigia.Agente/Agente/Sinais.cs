namespace Vigia.Agente.Agente;

public sealed record ErroRecente(DateTimeOffset Quando, string Rota, string Tipo, string Mensagem);

public sealed record SinaisDaAplicacao(
    bool Saudavel,
    string StatusDaSaude,
    int TotalDeErros,
    IReadOnlyList<string> PrincipaisErros)
{
    public bool PrecisaDeAtencao(int limiteDeErros) => !Saudavel || TotalDeErros >= limiteDeErros;

    /// <summary>Identifica o "tipo" de problema para não abrir o mesmo incidente repetidas vezes.</summary>
    public string Assinatura() =>
        $"{StatusDaSaude}|{string.Join(",", PrincipaisErros.Select(e => e.Split(':')[0]).Order())}";
}
