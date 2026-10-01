using EuleApi.Clients;
using EuleApi.Indicadores;
using EuleApi.Infrastructure;
using EuleApi.Models;
using EuleApi.Options;
using EuleApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EuleApi.Tests;

public class CnpjTests
{
    [Theory]
    [InlineData("66.503.941/0001-56")]
    [InlineData("66503941000156")]
    [InlineData("66.503.941%2F0001-56")]
    [InlineData("68.080.907/0001-41")]
    public void Aceita_com_ou_sem_mascara(string entrada) =>
        Assert.Equal(14, Cnpj.Normalizar(entrada).Length);

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("11111111111111")]
    [InlineData("66503941000157")]
    public void Rejeita_invalido(string entrada) =>
        Assert.Throws<CnpjInvalidoException>(() => Cnpj.Normalizar(entrada));
}

public class IndicadoresTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 30);
    private static readonly IOptions<ScoreOptions> Opts = Microsoft.Extensions.Options.Options.Create(new ScoreOptions());

    private static ContextoAvaliacao Ctx(DateOnly abertura, decimal capital, string cnae, bool autorizado, bool noRanking) =>
        new(new EmpresaReceita("x", "EMPRESA TESTE LTDA", null, abertura, capital, "ME", null, "Ativa", cnae, "desc", [],
                null, null, null, null, "São Paulo", "SP", []),
            new AutorizacaoBacen(autorizado, autorizado ? "Autorizada em Atividade" : null, null, null, "2026-09-30"),
            new ResultadoRanking(noRanking, noRanking ? new EntradaRanking("x", null, 1m, 1) : null, "2º trimestre de 2026", 100),
            Hoje);

    [Fact]
    public void Indicador01_empresa_muito_nova_com_capital_baixo_pontua_quase_o_maximo()
    {
        var r = new ConstituicaoRecenteCapitalIncompativel(Opts).Avaliar(Ctx(new(2026, 7, 17), 50_000m, "6201501", false, false));
        Assert.Equal(25, r.ScoreObtido);
    }

    [Fact]
    public void Indicador01_empresa_antiga_nao_pontua()
    {
        var r = new ConstituicaoRecenteCapitalIncompativel(Opts).Avaliar(Ctx(new(2015, 1, 1), 1_000m, "6201501", false, false));
        Assert.Equal(0, r.ScoreObtido);
    }

    [Theory]
    [InlineData("6492100", 0)]
    [InlineData("6619302", 0)]
    [InlineData("7319003", 20)]
    [InlineData("6201501", 20)]
    public void Indicador03_heuristica_de_grupo(string cnae, int esperado) =>
        Assert.Equal(esperado, new InconsistenciaCnae().Avaliar(Ctx(new(2026, 1, 1), 1, cnae, false, false)).ScoreObtido);

    [Fact]
    public void Indicador02_segue_autorizacao() =>
        Assert.Equal((25, 0),
            (new AusenciaAutorizacaoBacen().Avaliar(Ctx(new(2026, 1, 1), 1, "1", false, false)).ScoreObtido,
             new AusenciaAutorizacaoBacen().Avaliar(Ctx(new(2026, 1, 1), 1, "1", true, false)).ScoreObtido));

    [Fact]
    public void Indicador05_ausencia_atenuada_para_empresa_com_menos_de_3_meses()
    {
        var nova = new AusenciaRanking(Opts).Avaliar(Ctx(new(2026, 7, 17), 1, "1", false, false));
        var antiga = new AusenciaRanking(Opts).Avaliar(Ctx(new(2026, 1, 1), 1, "1", false, false));
        var presente = new AusenciaRanking(Opts).Avaliar(Ctx(new(2026, 1, 1), 1, "1", false, true));
        Assert.Equal((2, 10, 0), (nova.ScoreObtido, antiga.ScoreObtido, presente.ScoreObtido));
    }

    [Fact]
    public void Indicadores_04_e_06_sao_manuais()
    {
        var ctx = Ctx(new(2026, 1, 1), 1, "1", false, false);
        Assert.All([new CruzamentoSocietario().Avaliar(ctx), new InconsistenciaGeografica().Avaliar(ctx)],
            r => { Assert.True(r.RevisaoManual); Assert.Equal(0, r.ScoreObtido); });
    }
}

public class RankingCsvTests
{
    private const string Csv =
        "Ano;Trimestre;Categoria;Tipo;CNPJ IF;Instituição financeira;Índice;Quantidade total;\r\n" +
        "2026;2º;Top;Conglomerado; ;ALFA PAGAMENTOS (conglomerado);16,00;10;\r\n" +
        "2026;2º;Demais;Banco/financeira;62073200;BANCO BETA S.A.;362,43;10;\r\n" +
        "2026;2º;Demais;Conglomerado; ;GAMA IP (conglomerado); ;10;\r\n";

    [Fact]
    public void Calcula_posicao_pelo_indice_decrescente_e_trata_indice_vazio()
    {
        var e = RankingCsv.Parse(Csv);
        Assert.Equal(3, e.Count);
        Assert.Equal(2, e[0].Posicao);
        Assert.Equal(1, e[1].Posicao);
        Assert.Null(e[2].Posicao);
        Assert.Equal("62073200", e[1].Cnpj8);
    }

    [Fact]
    public void Localiza_por_cnpj_ou_por_nome_de_conglomerado()
    {
        var e = RankingCsv.Parse(Csv);
        Assert.Equal("BANCO BETA S.A.", RankingCsv.Localizar(e, "62073200", "outro nome")?.Nome);
        Assert.Equal("ALFA PAGAMENTOS (conglomerado)", RankingCsv.Localizar(e, "00000000", "Alfa Pagamentos S.A.")?.Nome);
        Assert.Null(RankingCsv.Localizar(e, "00000000", "Caitec LTDA"));
    }
}

public class EmpresaServiceTests
{
    private class FakeReceita : IReceitaClient
    {
        public int Chamadas;
        public Task<EmpresaReceita> ConsultarAsync(string cnpj14, CancellationToken ct)
        {
            Chamadas++;
            return Task.FromResult(new EmpresaReceita(cnpj14, "CAITEC LTDA", null, new(2026, 4, 28), 100_000m, "ME", null, "Ativa",
                "7319003", "Marketing direto", [], null, null, null, null, "São Paulo", "SP",
                [new SocioReceita("LORRANE", "Sócio-Administrador", "Pessoa Física", null, null, "***293137**")]));
        }
    }

    private class FakeBacen : IBacenAutorizacaoClient
    {
        public Task<AutorizacaoBacen> ConsultarAsync(string cnpj14, CancellationToken ct) =>
            Task.FromResult(new AutorizacaoBacen(false, null, null, null, "2026-09-30"));
    }

    private class FakeRanking : IRankingReclamacoesClient
    {
        public Task<ResultadoRanking> ConsultarAsync(string cnpj14, string razaoSocial, CancellationToken ct) =>
            Task.FromResult(new ResultadoRanking(false, null, "2º trimestre de 2026", 190));
    }

    private static (EmpresaService Service, FakeReceita Receita) Criar()
    {
        var score = Microsoft.Extensions.Options.Options.Create(new ScoreOptions());
        var receita = new FakeReceita();
        IIndicador[] indicadores =
        [
            new ConstituicaoRecenteCapitalIncompativel(score), new AusenciaAutorizacaoBacen(), new InconsistenciaCnae(),
            new CruzamentoSocietario(), new AusenciaRanking(score), new InconsistenciaGeografica(),
        ];
        var svc = new EmpresaService(receita, new FakeBacen(), new FakeRanking(), indicadores,
            new MemoryCache(new MemoryCacheOptions()), Microsoft.Extensions.Options.Options.Create(new CacheOptions()), score,
            NullLogger<EmpresaService>.Instance, new FixedClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        return (svc, receita);
    }

    private class FixedClock(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    [Fact]
    public async Task Soma_indicadores_e_classifica_risco()
    {
        var (svc, _) = Criar();
        var r = await svc.ConsultarAsync("66.503.941/0001-56", false, default);

        Assert.Equal(r.Indicadores.Sum(i => i.ScoreObtido), r.ScoreTotal);
        Assert.Equal(6, r.Indicadores.Count);
        Assert.True(r.ScoreParcial);
        Assert.Equal("ALTO", r.NivelRisco); // 24 + 25 + 20 + 0 + 10 + 0 = 79
        Assert.Equal("66503941000156", r.Cnpj);
        Assert.Contains("Ausente", r.RankingObservacao);
        Assert.Null(r.PosicaoRankingReclamacoes);
    }

    [Fact]
    public async Task Segunda_consulta_usa_cache_e_forcar_atualizacao_ignora()
    {
        var (svc, receita) = Criar();
        await svc.ConsultarAsync("66503941000156", false, default);
        await svc.ConsultarAsync("66.503.941/0001-56", false, default);
        Assert.Equal(1, receita.Chamadas);

        await svc.ConsultarAsync("66503941000156", true, default);
        Assert.Equal(2, receita.Chamadas);
    }

    [Fact]
    public async Task Cnpj_invalido_lanca_antes_de_consultar_fontes()
    {
        var (svc, receita) = Criar();
        await Assert.ThrowsAsync<CnpjInvalidoException>(() => svc.ConsultarAsync("123", false, default));
        Assert.Equal(0, receita.Chamadas);
    }
}

public class FimDeTrimestreTests
{
    [Theory]
    [InlineData(2026, 9, 30, 2026, 9, 30)]
    [InlineData(2026, 10, 5, 2026, 9, 30)]
    [InlineData(2026, 8, 15, 2026, 6, 30)]
    [InlineData(2026, 1, 10, 2025, 12, 31)]
    public void Calcula_ultimo_fim_de_trimestre(int y, int m, int d, int ey, int em, int ed) =>
        Assert.Equal(new DateOnly(ey, em, ed), BacenAutorizacaoClient.UltimoFimDeTrimestre(new DateOnly(y, m, d)));
}
