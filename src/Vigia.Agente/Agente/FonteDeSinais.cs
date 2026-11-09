using System.Net.Http.Json;

namespace Vigia.Agente.Agente;

/// <summary>
/// De onde o agente tira os sinais. Aqui é HTTP direto na aplicação.
/// Em produção, troque por consultas ao Application Insights / Log Analytics.
/// </summary>
public sealed class FonteDeSinais(HttpClient http)
{
    public async Task<(bool Saudavel, string Status)> VerificarSaudeAsync(CancellationToken ct)
    {
        try
        {
            using var resposta = await http.GetAsync("/health", ct);
            var corpo = await resposta.Content.ReadAsStringAsync(ct);
            return (resposta.IsSuccessStatusCode, $"{(int)resposta.StatusCode} {corpo}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (false, $"Sem resposta: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ErroRecente>> ConsultarErrosAsync(int minutos, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<List<ErroRecente>>($"/diagnostico/erros?minutos={minutos}", ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return [];
        }
    }

    public async Task<SinaisDaAplicacao> ColetarAsync(int janelaEmMinutos, CancellationToken ct)
    {
        var saude = await VerificarSaudeAsync(ct);
        var erros = await ConsultarErrosAsync(janelaEmMinutos, ct);

        var principais = erros
            .GroupBy(e => $"{e.Tipo}: {e.Mensagem}")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        return new SinaisDaAplicacao(saude.Saudavel, saude.Saudavel ? "Healthy" : "Unhealthy", erros.Count, principais);
    }
}
