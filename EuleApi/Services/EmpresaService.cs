using System.Collections.Concurrent;
using EuleApi.Clients;
using EuleApi.Indicadores;
using EuleApi.Infrastructure;
using EuleApi.Models;
using EuleApi.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EuleApi.Services;

public interface IEmpresaService
{
    Task<EmpresaResponse> ConsultarAsync(string cnpj, bool forcarAtualizacao, CancellationToken ct);
}

public class EmpresaService(
    IReceitaClient receita,
    IBacenAutorizacaoClient bacen,
    IRankingReclamacoesClient ranking,
    ICguSancoesClient cgu,
    IPgfnClient pgfn,
    IDataJudClient dataJud,
    IEnumerable<IIndicador> indicadores,
    IMemoryCache cache,
    IOptions<CacheOptions> cacheOptions,
    IOptions<ScoreOptions> scoreOptions,
    ILogger<EmpresaService> logger,
    TimeProvider clock) : IEmpresaService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<EmpresaResponse> ConsultarAsync(string cnpj, bool forcarAtualizacao, CancellationToken ct)
    {
        var cnpj14 = Cnpj.Normalizar(cnpj);
        var key = $"empresa:{cnpj14}";

        if (!forcarAtualizacao && cache.TryGetValue(key, out EmpresaResponse? hit) && hit is not null)
        {
            logger.LogInformation("Consulta cnpj={Cnpj} cache=HIT", cnpj14);
            return hit;
        }

        // Um único cálculo por CNPJ por vez: evita disparar N chamadas simultâneas às APIs públicas.
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (!forcarAtualizacao && cache.TryGetValue(key, out hit) && hit is not null)
                return hit;

            logger.LogInformation("Consulta cnpj={Cnpj} cache=MISS", cnpj14);
            var resposta = await MontarAsync(cnpj14, ct);

            // Só chegamos aqui se nenhuma fonte essencial falhou: erros nunca entram no cache.
            // As fontes complementares (07–09) entram no cache mesmo quando null.
            cache.Set(key, resposta, TimeSpan.FromHours(cacheOptions.Value.TtlHoras));
            return resposta;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<EmpresaResponse> MontarAsync(string cnpj14, CancellationToken ct)
    {
        // A Receita vem primeiro: CNPJ inexistente (404) encerra a consulta sem tocar no BACEN.
        var empresa = await receita.ConsultarAsync(cnpj14, ct);

        var autorizacaoTask = bacen.ConsultarAsync(cnpj14, ct);
        var rankingTask = ranking.ConsultarAsync(cnpj14, empresa.RazaoSocial, ct);
        var sancoesTask = Opcional("CGU CEIS/CNEP", () => cgu.ConsultarAsync(cnpj14, ct), ct);
        var pgfnTask = Opcional("PGFN", () => pgfn.ConsultarAsync(cnpj14, ct), ct);
        var dataJudTask = Opcional("DataJud", () => dataJud.ConsultarAsync(empresa.Socios, ct), ct);
        await Task.WhenAll(autorizacaoTask, rankingTask, sancoesTask, pgfnTask, dataJudTask);

        var agora = clock.GetUtcNow().UtcDateTime;
        var ctx = new ContextoAvaliacao(empresa, autorizacaoTask.Result, rankingTask.Result, DateOnly.FromDateTime(agora),
            sancoesTask.Result, pgfnTask.Result, dataJudTask.Result);

        var resultados = indicadores.Select(i => i.Avaliar(ctx)).OrderBy(i => i.Codigo, StringComparer.Ordinal).ToList();
        // Com os indicadores 07–09 a soma dos pesos passa de 100; o score é limitado à escala 0–100.
        var total = Math.Min(100, resultados.Sum(i => i.ScoreObtido));
        var o = scoreOptions.Value;
        var s = ctx.Sancoes;

        return new EmpresaResponse
        {
            Cnpj = cnpj14,
            CnpjFormatado = Cnpj.Formatar(cnpj14),
            RazaoSocial = empresa.RazaoSocial,
            NomeFantasia = empresa.NomeFantasia,
            DataAbertura = empresa.DataAbertura,
            CapitalSocial = empresa.CapitalSocial,
            Cnae = empresa.CnaeCodigo,
            CnaeDescricao = empresa.CnaeDescricao,
            Porte = empresa.Porte,
            Uf = empresa.Uf,
            Municipio = empresa.Municipio,
            SituacaoCadastral = empresa.SituacaoCadastral,
            Socios = empresa.Socios.Select(s => new SocioResponse
            {
                Nome = s.Nome,
                Qualificacao = s.Qualificacao,
                Tipo = s.Tipo,
                DataEntrada = s.DataEntrada,
                FaixaEtaria = s.FaixaEtaria,
                CpfCnpjMascarado = s.CpfCnpjMascarado,
            }).ToList(),
            AutorizadoBacen = ctx.Autorizacao.Autorizado,
            SituacaoBacen = ctx.Autorizacao.Situacao,
            ConstaNoRanking = ctx.Ranking.Encontrado,
            PosicaoRankingReclamacoes = ctx.Ranking.Entrada?.Posicao,
            RankingObservacao = ObservacaoRanking(ctx.Ranking),
            SancaoAtivaCeis = s?.Ceis,
            SancaoAtivaCnep = s?.Cnep,
            DescricaoSancao = s?.Descricao,
            PossuiDividaAtivaPgfn = ctx.Pgfn?.PossuiDivida,
            ValorDividaPgfn = ctx.Pgfn?.Valor,
            SocioComProcessoCriminal = ctx.DataJud?.SocioComProcessoCriminal,
            NomeSocioProcessado = ctx.DataJud?.NomeSocio,
            NumeroProcessosEncontrados = ctx.DataJud?.NumeroProcessos,
            Indicadores = resultados,
            ScoreTotal = total,
            ScoreParcial = resultados.Any(i => i.RevisaoManual),
            NivelRisco = ClassificarNivel(total, o),
            TipoAlertaGeral = ClassificarAlerta(total, resultados, o),
            ConsultadoEm = agora,
        };
    }

    public static string ClassificarNivel(int score, ScoreOptions o) =>
        score >= o.LimiarCritico ? "CRITICO" : score >= o.LimiarAlto ? "ALTO" : score >= o.LimiarMedio ? "MEDIO" : "BAIXO";

    public static string ClassificarAlerta(int score, IEnumerable<IndicadorResultado> resultados, ScoreOptions o)
    {
        var indicioJuridico = resultados.Any(i => i.Tipo == TiposIndicador.IndicioJuridico && i.ScoreObtido > 0);
        if (score >= o.LimiarFundamentoPic && indicioJuridico) return TiposAlerta.FundamentoPic;
        if (score >= o.LimiarSuspeitaQualificada && indicioJuridico) return TiposAlerta.SuspeitaQualificada;
        return score >= o.LimiarRiscoElevado ? TiposAlerta.RiscoElevado : TiposAlerta.RiscoModerado;
    }

    /// <summary>Executa uma fonte complementar: qualquer exceção vira null, sem interromper a consulta.</summary>
    private async Task<T?> Opcional<T>(string fonte, Func<Task<T?>> consulta, CancellationToken ct) where T : class
    {
        try
        {
            return await consulta();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Fonte complementar {Fonte} indisponível; campo retornará null", fonte);
            return null;
        }
    }

    private static string ObservacaoRanking(ResultadoRanking r)
    {
        if (!r.Encontrado)
            return $"Ausente do ranking de reclamações do BACEN ({r.Referencia}). O ranking é trimestral e tem defasagem: " +
                   "empresas recentes, não autorizadas ou sem reclamações analisadas no período podem não constar. " +
                   "Ausência não significa zero reclamações.";

        var e = r.Entrada!;
        return e.Posicao is int pos
            ? $"{pos}º de {r.TotalInstituicoes} instituições no ranking ({r.Referencia}); índice {e.Indice:0.##}."
            : $"Consta no ranking ({r.Referencia}), porém sem índice calculado (volume de reclamações/clientes insuficiente).";
    }
}
