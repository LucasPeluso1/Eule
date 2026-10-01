using System.Text.Json;
using EuleApi.Infrastructure;
using EuleApi.Models;
using EuleApi.Options;
using Microsoft.Extensions.Options;

namespace EuleApi.Clients;

public interface IBacenAutorizacaoClient
{
    Task<AutorizacaoBacen> ConsultarAsync(string cnpj14, CancellationToken ct);
}

/// <summary>
/// Cadastro de entidades supervisionadas do BACEN, via API Olinda (serviço BcBase v2, recurso EntidadesSupervisionadas).
/// Filtra pelo CNPJ raiz (8 dígitos): a autorização é da pessoa jurídica, não do estabelecimento.
/// </summary>
public class BacenAutorizacaoClient(HttpClient http, IOptions<FontesOptions> options, ILogger<BacenAutorizacaoClient> logger)
    : IBacenAutorizacaoClient
{
    private const string Fonte = "BACEN – Entidades Supervisionadas (Olinda BcBase v2)";

    public async Task<AutorizacaoBacen> ConsultarAsync(string cnpj14, CancellationToken ct)
    {
        var raiz = cnpj14[..8];
        var dataBase = UltimoFimDeTrimestre(DateOnly.FromDateTime(DateTime.UtcNow));
        var ultima = dataBase;

        // A data-base mais recente pode ainda não estar publicada: se vier vazio, tenta o trimestre anterior
        // antes de concluir que a entidade não consta no cadastro.
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            var itens = await BuscarAsync(raiz, dataBase, ct);
            var autorizada = itens.FirstOrDefault(i => i.Situacao?.StartsWith("Autorizada", StringComparison.OrdinalIgnoreCase) == true)
                             ?? itens.FirstOrDefault();

            if (autorizada is not null)
            {
                return new AutorizacaoBacen(
                    Autorizado: autorizada.Situacao?.StartsWith("Autorizada", StringComparison.OrdinalIgnoreCase) == true,
                    Situacao: autorizada.Situacao,
                    Tipo: autorizada.Tipo,
                    Nome: autorizada.Nome,
                    DataBase: dataBase.ToString("yyyy-MM-dd"));
            }

            ultima = dataBase;
            dataBase = UltimoFimDeTrimestre(dataBase.AddDays(-1));
        }

        return new AutorizacaoBacen(false, null, null, null, ultima.ToString("yyyy-MM-dd"));
    }

    private record Item(string? Nome, string? Situacao, string? Tipo);

    private async Task<List<Item>> BuscarAsync(string raiz, DateOnly dataBase, CancellationToken ct)
    {
        var baseUrl = options.Value.BacenOlindaBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/EntidadesSupervisionadas(dataBase=@dataBase)" +
                  $"?@dataBase='{dataBase:MM-dd-yyyy}'" +
                  $"&$filter={Uri.EscapeDataString($"codigoCNPJ8 eq '{raiz}'")}" +
                  "&$select=nomeEntidadeInteresse,descricaoTipoSituacaoPessoaJuridica,descricaoTipoEntidadeSupervisionada" +
                  "&$format=json";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var resp = await http.GetAsync(url, ct);
            logger.LogInformation("Fonte {Fonte} raiz={Raiz} dataBase={Db} status={Status} latenciaMs={Ms}", Fonte, raiz, dataBase, (int)resp.StatusCode, sw.ElapsedMilliseconds);

            if (!resp.IsSuccessStatusCode)
                throw new FonteIndisponivelException(Fonte, $"Resposta inesperada (HTTP {(int)resp.StatusCode}).");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                throw new FonteIndisponivelException(Fonte, "Formato de resposta inesperado.");

            return value.EnumerateArray().Select(x => new Item(
                Texto(x, "nomeEntidadeInteresse"),
                Texto(x, "descricaoTipoSituacaoPessoaJuridica"),
                Texto(x, "descricaoTipoEntidadeSupervisionada"))).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !ct.IsCancellationRequested)
        {
            throw new FonteIndisponivelException(Fonte, "Falha de comunicação ou resposta inválida.", ex);
        }
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Último dia de trimestre (31/03, 30/06, 30/09, 31/12) menor ou igual à data informada.</summary>
    public static DateOnly UltimoFimDeTrimestre(DateOnly data)
    {
        var fimTrimestreAtual = new DateOnly(data.Year, ((data.Month - 1) / 3 + 1) * 3, 1).AddMonths(1).AddDays(-1);
        return fimTrimestreAtual <= data ? fimTrimestreAtual : new DateOnly(data.Year, ((data.Month - 1) / 3) * 3 + 1, 1).AddDays(-1);
    }
}
