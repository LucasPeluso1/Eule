using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EuleApi.Infrastructure;
using EuleApi.Models;
using EuleApi.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EuleApi.Clients;

public interface IRankingReclamacoesClient
{
    Task<ResultadoRanking> ConsultarAsync(string cnpj14, string razaoSocial, CancellationToken ct);
}

/// <summary>Índice do ranking já processado, compartilhado entre consultas.</summary>
public record IndiceRanking(string Referencia, IReadOnlyList<EntradaRanking> Entradas);

/// <summary>
/// Ranking de Reclamações do BACEN. Descobre o ranking trimestral mais recente na lista JSON do serviço rdrweb,
/// baixa o CSV e o processa em memória. O índice (um por trimestre) é cacheado, sem rebaixar o CSV a cada CNPJ.
/// </summary>
public class RankingReclamacoesClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<FontesOptions> options,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RankingReclamacoesClient> logger) : IRankingReclamacoesClient
{
    private const string Fonte = "BACEN – Ranking de Reclamações (rdrweb)";
    private const string CacheKey = "ranking:indice";
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<ResultadoRanking> ConsultarAsync(string cnpj14, string razaoSocial, CancellationToken ct)
    {
        var indice = await ObterIndiceAsync(ct);
        var entrada = RankingCsv.Localizar(indice.Entradas, cnpj14[..8], razaoSocial);
        return new ResultadoRanking(entrada is not null, entrada, indice.Referencia, indice.Entradas.Count);
    }

    private async Task<IndiceRanking> ObterIndiceAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IndiceRanking? pronto) && pronto is not null) return pronto;

        await Lock.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(CacheKey, out pronto) && pronto is not null) return pronto;

            var indice = await CarregarAsync(ct);
            cache.Set(CacheKey, indice, TimeSpan.FromHours(cacheOptions.Value.TtlHoras));
            return indice;
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<IndiceRanking> CarregarAsync(CancellationToken ct)
    {
        var baseUrl = options.Value.BacenRankingBaseUrl.TrimEnd('/') + "/";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Lista de rankings existentes → escolhe os trimestrais mais recentes do tipo configurado.
            using var listResp = await http.GetAsync(baseUrl, ct);
            if (!listResp.IsSuccessStatusCode)
                throw new FonteIndisponivelException(Fonte, $"Lista de rankings indisponível (HTTP {(int)listResp.StatusCode}).");

            using var doc = await JsonDocument.ParseAsync(await listResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var candidatos = RankingCsv.ListarTrimestrais(doc.RootElement, options.Value.RankingTipo);
            if (candidatos.Count == 0)
                throw new FonteIndisponivelException(Fonte, "Nenhum ranking trimestral encontrado na lista do BACEN.");

            // 2. Baixa o mais recente; se falhar/vier vazio, tenta os dois anteriores.
            foreach (var (ano, periodo) in candidatos.Take(3))
            {
                var url = $"{baseUrl}arquivo?ano={ano}&periodicidade=TRIMESTRAL&periodo={periodo}&tipo={Uri.EscapeDataString(options.Value.RankingTipo).Replace("%20", "+")}";
                using var csvResp = await http.GetAsync(url, ct);
                logger.LogInformation("Fonte {Fonte} ranking={Ano}T{Periodo} status={Status} latenciaMs={Ms}", Fonte, ano, periodo, (int)csvResp.StatusCode, sw.ElapsedMilliseconds);
                if (!csvResp.IsSuccessStatusCode) continue;

                var bytes = await csvResp.Content.ReadAsByteArrayAsync(ct);
                var entradas = RankingCsv.Parse(Encoding.Latin1.GetString(bytes));
                if (entradas.Count > 0)
                    return new IndiceRanking($"{periodo}º trimestre de {ano}", entradas);
            }

            throw new FonteIndisponivelException(Fonte, "Não foi possível baixar o CSV do ranking mais recente.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !ct.IsCancellationRequested)
        {
            throw new FonteIndisponivelException(Fonte, "Falha de comunicação ou resposta inválida.", ex);
        }
    }
}

/// <summary>Parsing puro do ranking (testável sem rede).</summary>
public static partial class RankingCsv
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>Lê o CSV (separador ';', decimais pt-BR) e calcula a posição pelo índice, do maior para o menor.</summary>
    public static List<EntradaRanking> Parse(string csv)
    {
        var linhas = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        if (linhas.Count < 2) return [];

        var cab = linhas[0].Split(';').Select(c => c.Trim()).ToList();
        int iCnpj = cab.FindIndex(c => c.StartsWith("CNPJ", StringComparison.OrdinalIgnoreCase));
        int iNome = cab.FindIndex(c => c.StartsWith("Institui", StringComparison.OrdinalIgnoreCase));
        int iIndice = cab.FindIndex(c => c.StartsWith("Índice", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Indice", StringComparison.OrdinalIgnoreCase));
        if (iCnpj < 0 || iNome < 0 || iIndice < 0) return [];

        var brutas = new List<(string Nome, string? Cnpj8, decimal? Indice)>();
        foreach (var linha in linhas.Skip(1))
        {
            var campos = linha.Split(';');
            if (campos.Length <= Math.Max(iNome, Math.Max(iCnpj, iIndice))) continue;

            var nome = campos[iNome].Trim();
            if (nome.Length == 0) continue;

            var cnpj = new string(campos[iCnpj].Where(char.IsDigit).ToArray());
            var cnpj8 = cnpj.Length >= 8 ? cnpj[..8] : null;

            decimal? indice = decimal.TryParse(campos[iIndice].Trim(), NumberStyles.Number, PtBr, out var v) ? v : null;
            brutas.Add((nome, cnpj8, indice));
        }

        var ordenados = brutas.Where(b => b.Indice.HasValue).OrderByDescending(b => b.Indice!.Value).Select(b => b.Indice!.Value).ToList();
        return brutas.Select(b => new EntradaRanking(
            b.Nome,
            b.Cnpj8,
            b.Indice,
            b.Indice.HasValue ? ordenados.IndexOf(b.Indice.Value) + 1 : null)).ToList();
    }

    /// <summary>
    /// Procura por CNPJ raiz (quando o CSV o informa) ou, para conglomerados, pelo nome normalizado.
    /// O CSV só traz CNPJ para parte das linhas; conglomerados aparecem só por nome.
    /// </summary>
    public static EntradaRanking? Localizar(IReadOnlyList<EntradaRanking> entradas, string cnpj8, string razaoSocial)
    {
        var porCnpj = entradas.FirstOrDefault(e => e.Cnpj8 == cnpj8);
        if (porCnpj is not null) return porCnpj;

        var alvo = NormalizarNome(razaoSocial);
        if (alvo.Length < 8) return null;

        return entradas.FirstOrDefault(e =>
        {
            var n = NormalizarNome(e.Nome);
            return n == alvo || (n.Length >= 10 && (n.StartsWith(alvo) || alvo.StartsWith(n)));
        });
    }

    public static string NormalizarNome(string nome)
    {
        var s = nome.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        s = new string(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        s = Conglomerado().Replace(s, " ");
        s = NaoAlfanumerico().Replace(s, " ");
        s = Sufixos().Replace(s, " ");
        return Espacos().Replace(s, " ").Trim();
    }

    /// <summary>(ano, periodo) dos rankings trimestrais do tipo informado, do mais recente para o mais antigo.</summary>
    public static List<(int Ano, int Periodo)> ListarTrimestrais(JsonElement raiz, string tipo)
    {
        var lista = new List<(int, int)>();
        if (!raiz.TryGetProperty("anos", out var anos)) return lista;

        foreach (var a in anos.EnumerateArray())
        {
            if (!int.TryParse(a.GetProperty("ano").GetString(), out var ano)) continue;
            foreach (var p in a.GetProperty("periodicidades").EnumerateArray())
            {
                if (!string.Equals(p.GetProperty("periodicidade").GetString(), "TRIMESTRAL", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var per in p.GetProperty("periodos").EnumerateArray())
                {
                    var tem = per.GetProperty("tipos").EnumerateArray()
                        .Any(t => string.Equals(t.GetProperty("tipo").GetString(), tipo, StringComparison.OrdinalIgnoreCase));
                    if (tem) lista.Add((ano, per.GetProperty("periodo").GetInt32()));
                }
            }
        }

        return lista.OrderByDescending(x => x.Item1).ThenByDescending(x => x.Item2).ToList();
    }

    [GeneratedRegex(@"\(\s*CONGLOMERADO\s*\)")] private static partial Regex Conglomerado();
    [GeneratedRegex(@"[^A-Z0-9 ]")] private static partial Regex NaoAlfanumerico();
    [GeneratedRegex(@"\b(S A|SA|LTDA|EIRELI|ME|EPP|SCFI|IP)\b")] private static partial Regex Sufixos();
    [GeneratedRegex(@"\s+")] private static partial Regex Espacos();
}
