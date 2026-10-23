using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EuleApi.Models;
using EuleApi.Options;
using Microsoft.Extensions.Options;

namespace EuleApi.Clients;

// Fontes complementares (indicadores 07–09). Diferente da Receita e do BACEN, uma falha aqui não interrompe a
// consulta: o EmpresaService captura a exceção e o campo correspondente da resposta fica null.

public interface ICguSancoesClient
{
    Task<ResultadoSancoes?> ConsultarAsync(string cnpj14, CancellationToken ct);
}

public interface IPgfnClient
{
    Task<ResultadoPgfn?> ConsultarAsync(string cnpj14, CancellationToken ct);
}

public interface IDataJudClient
{
    Task<ResultadoDataJud?> ConsultarAsync(IReadOnlyList<SocioReceita> socios, CancellationToken ct);
}

/// <summary>CEIS e CNEP do Portal da Transparência (CGU). As duas listas são consultadas em paralelo.</summary>
public class CguSancoesClient(HttpClient http, IOptions<FontesOptions> options, ILogger<CguSancoesClient> logger) : ICguSancoesClient
{
    public async Task<ResultadoSancoes?> ConsultarAsync(string cnpj14, CancellationToken ct)
    {
        var ceisTask = ConsultarListaAsync("ceis", cnpj14, ct);
        var cnepTask = ConsultarListaAsync("cnep", cnpj14, ct);
        await Task.WhenAll(ceisTask, cnepTask);

        var (ceis, descCeis) = ceisTask.Result;
        var (cnep, descCnep) = cnepTask.Result;
        if (ceis is null && cnep is null) return null;

        var descricao = string.Join(" | ", new[] { descCeis, descCnep }.Where(d => d is not null));
        return new ResultadoSancoes(ceis, cnep, descricao.Length == 0 ? null : descricao);
    }

    /// <summary>(true/false, descrição da primeira sanção) ou (null, null) se a lista não pôde ser consultada.</summary>
    private async Task<(bool?, string?)> ConsultarListaAsync(string lista, string cnpj14, CancellationToken ct)
    {
        var url = $"{options.Value.CguBaseUrl.TrimEnd('/')}/{lista}?cnpjSancionado={cnpj14}&pagina=1&tamanhoPagina=5";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("chave-api-dados", options.Value.CguChaveApi);
            req.Headers.Accept.ParseAdd("application/json");

            using var resp = await http.SendAsync(req, ct);
            logger.LogInformation("Fonte CGU-{Lista} cnpj={Cnpj} status={Status} latenciaMs={Ms}", lista.ToUpperInvariant(), cnpj14, (int)resp.StatusCode, sw.ElapsedMilliseconds);
            resp.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return FontesJson.LerSancoes(doc.RootElement, lista.ToUpperInvariant());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fonte CGU-{Lista} indisponível para cnpj={Cnpj}", lista.ToUpperInvariant(), cnpj14);
            return (null, null);
        }
    }
}

/// <summary>Lista de devedores da PGFN (dívida ativa da União).</summary>
public class PgfnClient(HttpClient http, IOptions<FontesOptions> options, ILogger<PgfnClient> logger) : IPgfnClient
{
    public async Task<ResultadoPgfn?> ConsultarAsync(string cnpj14, CancellationToken ct)
    {
        var url = $"{options.Value.PgfnUrl}?cnpj={cnpj14}";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var resp = await http.GetAsync(url, ct);
        logger.LogInformation("Fonte PGFN cnpj={Cnpj} status={Status} latenciaMs={Ms}", cnpj14, (int)resp.StatusCode, sw.ElapsedMilliseconds);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var total = FontesJson.LerTotalDivida(doc.RootElement);
        return total is decimal v ? new ResultadoPgfn(v > 0, v) : null;
    }
}

/// <summary>
/// API pública do DataJud (CNJ), índice do TJBA. Consulta até N sócios pessoa física cujo CPF esteja completo;
/// a Receita normalmente devolve o CPF mascarado, e nesse caso não há o que consultar (resultado null).
/// </summary>
public class DataJudClient(HttpClient http, IOptions<FontesOptions> options, ILogger<DataJudClient> logger) : IDataJudClient
{
    public async Task<ResultadoDataJud?> ConsultarAsync(IReadOnlyList<SocioReceita> socios, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.DataJudApiKey))
        {
            logger.LogWarning("Fonte DataJud sem chave configurada (Fontes:DataJudApiKey); indicador 09 não avaliado");
            return null;
        }

        var consultaveis = socios
            .Select(s => (Socio: s, Cpf: FontesJson.CpfCompleto(s.CpfCnpjMascarado)))
            .Where(x => x.Cpf is not null)
            .Take(o.DataJudMaxSocios)
            .ToList();
        if (consultaveis.Count == 0) return null;

        var contagens = await Task.WhenAll(consultaveis.Select(x => ConsultarCpfAsync(x.Cpf!, ct)));
        var nomeProcessado = consultaveis.Zip(contagens).FirstOrDefault(x => x.Second > 0).First.Socio?.Nome;

        return new ResultadoDataJud(nomeProcessado is not null, nomeProcessado, contagens.Sum(), consultaveis.Count);
    }

    private async Task<int> ConsultarCpfAsync(string cpf, CancellationToken ct)
    {
        var o = options.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, o.DataJudUrl)
        {
            Content = JsonContent.Create(new { query = new { match = new { cpfCnpjAdvogado = cpf } } }),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {o.DataJudApiKey}");

        using var resp = await http.SendAsync(req, ct);
        logger.LogInformation("Fonte DataJud-TJBA status={Status} latenciaMs={Ms}", (int)resp.StatusCode, sw.ElapsedMilliseconds);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return FontesJson.ContarProcessosCriminais(doc.RootElement);
    }
}

/// <summary>Parsing puro das fontes complementares (testável sem rede).</summary>
public static class FontesJson
{
    private static readonly string[] TermosCriminais = ["CRIME", "FRAUDE", "ESTELIONATO"];

    public static (bool, string?) LerSancoes(JsonElement raiz, string lista)
    {
        if (raiz.ValueKind != JsonValueKind.Array)
            throw new JsonException($"{lista}: resposta não é uma lista.");
        if (raiz.GetArrayLength() == 0) return (false, null);

        var s = raiz[0];
        var tipo = Texto(s, "tipoSancao", "descricaoResumida") ?? Texto(s, "tipoSancao", "descricaoPortal") ?? "sanção";
        var orgao = Texto(s, "orgaoSancionador", "nome");
        var inicio = Texto(s, "dataInicioSancao");
        var fim = Texto(s, "dataFimSancao");

        var sb = new StringBuilder($"{lista}: {tipo}");
        if (orgao is not null) sb.Append($" – {orgao}");
        if (inicio is not null) sb.Append($" (início {inicio}{(fim is not null ? $", fim {fim}" : "")})");
        if (raiz.GetArrayLength() > 1) sb.Append($"; {raiz.GetArrayLength()} registros na primeira página");
        return (true, sb.ToString());
    }

    /// <summary>Lê <c>totalDivida</c> do objeto raiz ou soma o campo nos itens, se a resposta for uma lista.</summary>
    public static decimal? LerTotalDivida(JsonElement raiz)
    {
        if (raiz.ValueKind == JsonValueKind.Object)
            return Decimal(raiz, "totalDivida") ?? throw new JsonException("PGFN: campo totalDivida ausente.");

        if (raiz.ValueKind == JsonValueKind.Array)
            return raiz.EnumerateArray().Sum(i => Decimal(i, "totalDivida") ?? 0m);

        throw new JsonException("PGFN: formato de resposta inesperado.");
    }

    /// <summary>Quantos processos do resultado têm classe ou assunto com Crime, Fraude ou Estelionato.</summary>
    public static int ContarProcessosCriminais(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("hits", out var hits) || !hits.TryGetProperty("hits", out var lista) || lista.ValueKind != JsonValueKind.Array)
            throw new JsonException("DataJud: formato de resposta inesperado.");

        return lista.EnumerateArray().Count(h =>
        {
            if (!h.TryGetProperty("_source", out var src) || src.ValueKind != JsonValueKind.Object) return false;
            return src.EnumerateObject()
                .Where(p => p.Name.StartsWith("classe", StringComparison.OrdinalIgnoreCase) || p.Name.StartsWith("assunto", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => Textos(p.Value))
                .Select(Normalizar)
                .Any(t => TermosCriminais.Any(t.Contains));
        });
    }

    /// <summary>CPF com 11 dígitos sem máscara; null para CPF mascarado (***123456**), CNPJ ou ausente.</summary>
    public static string? CpfCompleto(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc) || doc.Contains('*')) return null;
        var digitos = new string(doc.Where(char.IsDigit).ToArray());
        return digitos.Length == 11 ? digitos : null;
    }

    private static IEnumerable<string> Textos(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Textos),
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Textos(p.Value)),
        _ => [],
    };

    private static string Normalizar(string s)
    {
        var d = s.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static string? Texto(JsonElement e, params string[] caminho)
    {
        foreach (var nome in caminho)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nome, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString()!.Trim() : null;
    }

    private static decimal? Decimal(JsonElement e, string nome)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nome, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString() ?? "";
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(s.Replace("R$", "").Trim(), NumberStyles.Number, new CultureInfo("pt-BR"), out v)) return v;
        }
        return null;
    }
}
