using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using EuleApi.Infrastructure;
using EuleApi.Models;
using Microsoft.Extensions.Options;
using EuleApi.Options;

namespace EuleApi.Clients;

public interface IReceitaClient
{
    Task<EmpresaReceita> ConsultarAsync(string cnpj14, CancellationToken ct);
}

/// <summary>Receita Federal via publica.cnpj.ws (gratuita; limite ~3 req/min por IP).</summary>
public class ReceitaClient(HttpClient http, IOptions<FontesOptions> options, ILogger<ReceitaClient> logger) : IReceitaClient
{
    private const string Fonte = "Receita Federal (publica.cnpj.ws)";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<EmpresaReceita> ConsultarAsync(string cnpj14, CancellationToken ct)
    {
        var url = options.Value.ReceitaBaseUrl.TrimEnd('/') + "/" + cnpj14;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var resp = await http.GetAsync(url, ct);
            logger.LogInformation("Fonte {Fonte} cnpj={Cnpj} status={Status} latenciaMs={Ms}", Fonte, cnpj14, (int)resp.StatusCode, sw.ElapsedMilliseconds);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new CnpjNaoEncontradoException(Cnpj.Formatar(cnpj14));
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                throw new FonteIndisponivelException(Fonte, "Limite de requisições atingido (cerca de 3 por minuto). Tente novamente em instantes.");
            if (!resp.IsSuccessStatusCode)
                throw new FonteIndisponivelException(Fonte, $"Resposta inesperada (HTTP {(int)resp.StatusCode}).");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var dto = await JsonSerializer.DeserializeAsync<ReceitaDto>(stream, Json, ct)
                      ?? throw new FonteIndisponivelException(Fonte, "Resposta vazia.");
            return Mapear(dto, cnpj14);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !ct.IsCancellationRequested)
        {
            throw new FonteIndisponivelException(Fonte, "Falha de comunicação ou resposta inválida.", ex);
        }
    }

    private static EmpresaReceita Mapear(ReceitaDto d, string cnpj14)
    {
        var e = d.Estabelecimento ?? throw new FonteIndisponivelException(Fonte, "Resposta sem dados do estabelecimento.");

        var capital = decimal.TryParse(d.CapitalSocial, NumberStyles.Number, CultureInfo.InvariantCulture, out var c) ? c : 0m;
        var abertura = DateOnly.TryParse(e.DataInicioAtividade, CultureInfo.InvariantCulture, out var a) ? a : default;

        var logradouro = string.Join(' ', new[] { e.TipoLogradouro, e.Logradouro }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new EmpresaReceita(
            Cnpj: e.Cnpj ?? cnpj14,
            RazaoSocial: d.RazaoSocial?.Trim() ?? "",
            NomeFantasia: string.IsNullOrWhiteSpace(e.NomeFantasia) ? null : e.NomeFantasia.Trim(),
            DataAbertura: abertura,
            CapitalSocial: capital,
            Porte: d.Porte?.Descricao?.Trim(),
            NaturezaJuridica: d.NaturezaJuridica?.Descricao?.Trim(),
            SituacaoCadastral: e.SituacaoCadastral?.Trim() ?? "Desconhecida",
            CnaeCodigo: e.AtividadePrincipal?.Id ?? "",
            CnaeDescricao: e.AtividadePrincipal?.Descricao?.Trim() ?? "",
            CnaesSecundarios: (e.AtividadesSecundarias ?? []).Select(x => $"{x.Id} - {x.Descricao}").ToList(),
            Logradouro: logradouro,
            Numero: e.Numero,
            Bairro: e.Bairro,
            Cep: e.Cep,
            Municipio: e.Cidade?.Nome,
            Uf: e.Estado?.Sigla,
            Socios: (d.Socios ?? []).Select(s => new SocioReceita(
                Nome: s.Nome?.Trim() ?? "",
                Qualificacao: s.QualificacaoSocio?.Descricao?.Trim() ?? "",
                Tipo: s.Tipo,
                DataEntrada: DateOnly.TryParse(s.DataEntrada, CultureInfo.InvariantCulture, out var de) ? de : null,
                FaixaEtaria: s.FaixaEtaria,
                CpfCnpjMascarado: s.CpfCnpjSocio)).ToList());
    }

    // ---- DTOs da resposta de publica.cnpj.ws (snake_case) ----

    private class ReceitaDto
    {
        public string? RazaoSocial { get; set; }
        public string? CapitalSocial { get; set; }
        public DescricaoDto? Porte { get; set; }
        public DescricaoDto? NaturezaJuridica { get; set; }
        public List<SocioDto>? Socios { get; set; }
        public EstabelecimentoDto? Estabelecimento { get; set; }
    }

    private class DescricaoDto
    {
        public string? Descricao { get; set; }
    }

    private class SocioDto
    {
        public string? CpfCnpjSocio { get; set; }
        public string? Nome { get; set; }
        public string? Tipo { get; set; }
        public string? DataEntrada { get; set; }
        public string? FaixaEtaria { get; set; }
        public DescricaoDto? QualificacaoSocio { get; set; }
    }

    private class AtividadeDto
    {
        public string? Id { get; set; }
        public string? Descricao { get; set; }
    }

    private class EstabelecimentoDto
    {
        public string? Cnpj { get; set; }
        public string? NomeFantasia { get; set; }
        public string? SituacaoCadastral { get; set; }
        public string? DataInicioAtividade { get; set; }
        public string? TipoLogradouro { get; set; }
        public string? Logradouro { get; set; }
        public string? Numero { get; set; }
        public string? Bairro { get; set; }
        public string? Cep { get; set; }
        public AtividadeDto? AtividadePrincipal { get; set; }
        public List<AtividadeDto>? AtividadesSecundarias { get; set; }
        public EstadoDto? Estado { get; set; }
        public CidadeDto? Cidade { get; set; }
    }

    private class EstadoDto
    {
        public string? Sigla { get; set; }
    }

    private class CidadeDto
    {
        public string? Nome { get; set; }
    }
}
