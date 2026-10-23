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

    private class FakeCgu(Func<ResultadoSancoes?> resultado) : ICguSancoesClient
    {
        public Task<ResultadoSancoes?> ConsultarAsync(string cnpj14, CancellationToken ct) => Task.FromResult(resultado());
    }

    private class FakePgfn(Func<ResultadoPgfn?> resultado) : IPgfnClient
    {
        public Task<ResultadoPgfn?> ConsultarAsync(string cnpj14, CancellationToken ct) => Task.FromResult(resultado());
    }

    private class FakeDataJud(Func<ResultadoDataJud?> resultado) : IDataJudClient
    {
        public Task<ResultadoDataJud?> ConsultarAsync(IReadOnlyList<SocioReceita> socios, CancellationToken ct) => Task.FromResult(resultado());
    }

    private static (EmpresaService Service, FakeReceita Receita) Criar(
        Func<ResultadoSancoes?>? cgu = null, Func<ResultadoPgfn?>? pgfn = null, Func<ResultadoDataJud?>? dataJud = null)
    {
        var score = Microsoft.Extensions.Options.Options.Create(new ScoreOptions());
        var receita = new FakeReceita();
        IIndicador[] indicadores =
        [
            new ConstituicaoRecenteCapitalIncompativel(score), new AusenciaAutorizacaoBacen(), new InconsistenciaCnae(),
            new CruzamentoSocietario(), new AusenciaRanking(score), new InconsistenciaGeografica(),
            new SancaoCeisCnep(), new DividaAtivaPgfn(), new SocioProcessoCriminal(),
        ];
        var svc = new EmpresaService(receita, new FakeBacen(), new FakeRanking(),
            new FakeCgu(cgu ?? (() => new ResultadoSancoes(false, false, null))),
            new FakePgfn(pgfn ?? (() => new ResultadoPgfn(false, 0m))),
            new FakeDataJud(dataJud ?? (() => null)),
            indicadores,
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
        Assert.Equal(9, r.Indicadores.Count);
        Assert.True(r.ScoreParcial);
        Assert.Equal("ALTO", r.NivelRisco); // 24 + 25 + 20 + 0 + 10 + 0 + 0 + 0 + 0 = 79
        Assert.Equal(TiposAlerta.RiscoElevado, r.TipoAlertaGeral); // sem indício jurídico positivo
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
    public async Task Fontes_complementares_com_falha_viram_null_e_resultado_vai_para_cache()
    {
        var (svc, receita) = Criar(
            cgu: () => throw new HttpRequestException("fora do ar"),
            pgfn: () => throw new TimeoutException(),
            dataJud: () => throw new InvalidOperationException());

        var r = await svc.ConsultarAsync("66503941000156", false, default);
        Assert.Null(r.SancaoAtivaCeis);
        Assert.Null(r.SancaoAtivaCnep);
        Assert.Null(r.PossuiDividaAtivaPgfn);
        Assert.Null(r.ValorDividaPgfn);
        Assert.Null(r.SocioComProcessoCriminal);
        Assert.Null(r.NumeroProcessosEncontrados);
        Assert.Contains("NÃO AVALIADO", r.Indicadores.Single(i => i.Codigo == "07").Fundamentacao);

        await svc.ConsultarAsync("66503941000156", false, default);
        Assert.Equal(1, receita.Chamadas);
    }

    [Fact]
    public async Task Indicio_juridico_positivo_com_score_alto_gera_fundamento_pic()
    {
        var (svc, _) = Criar(
            cgu: () => new ResultadoSancoes(true, false, "CEIS: Inidoneidade"),
            pgfn: () => new ResultadoPgfn(true, 12_345.67m));

        var r = await svc.ConsultarAsync("66503941000156", false, default);
        Assert.Equal(100, r.ScoreTotal); // 79 + 25 + 15 = 119, limitado a 100
        Assert.Equal("CRITICO", r.NivelRisco);
        Assert.Equal(TiposAlerta.FundamentoPic, r.TipoAlertaGeral);
        Assert.True(r.SancaoAtivaCeis);
        Assert.False(r.SancaoAtivaCnep);
        Assert.Equal("CEIS: Inidoneidade", r.DescricaoSancao);
        Assert.Equal(12_345.67m, r.ValorDividaPgfn);
    }

    [Fact]
    public async Task Cnpj_invalido_lanca_antes_de_consultar_fontes()
    {
        var (svc, receita) = Criar();
        await Assert.ThrowsAsync<CnpjInvalidoException>(() => svc.ConsultarAsync("123", false, default));
        Assert.Equal(0, receita.Chamadas);
    }
}

public class ClassificacaoTests
{
    private static readonly ScoreOptions O = new();

    private static IndicadorResultado Ind(string tipo, int score) => new()
    {
        Codigo = "x", Descricao = "x", Fundamentacao = "x", FonteConsultada = "x", PesoMaximo = 25, ScoreObtido = score, Tipo = tipo,
    };

    [Theory]
    [InlineData(0, "BAIXO")]
    [InlineData(39, "BAIXO")]
    [InlineData(40, "MEDIO")]
    [InlineData(69, "MEDIO")]
    [InlineData(70, "ALTO")]
    [InlineData(89, "ALTO")]
    [InlineData(90, "CRITICO")]
    [InlineData(100, "CRITICO")]
    public void Nivel_de_risco_por_faixa(int score, string esperado) =>
        Assert.Equal(esperado, EmpresaService.ClassificarNivel(score, O));

    [Theory]
    [InlineData(95, true, TiposAlerta.FundamentoPic)]
    [InlineData(90, true, TiposAlerta.FundamentoPic)]
    [InlineData(89, true, TiposAlerta.SuspeitaQualificada)]
    [InlineData(80, true, TiposAlerta.SuspeitaQualificada)]
    [InlineData(95, false, TiposAlerta.RiscoElevado)]
    [InlineData(79, true, TiposAlerta.RiscoElevado)]
    [InlineData(60, false, TiposAlerta.RiscoElevado)]
    [InlineData(59, true, TiposAlerta.RiscoModerado)]
    public void Tipo_de_alerta_exige_indicio_juridico_para_pic_e_suspeita(int score, bool indicio, string esperado)
    {
        IndicadorResultado[] resultados =
        [
            Ind(TiposIndicador.FatorRisco, 25),
            Ind(TiposIndicador.IndicioJuridico, indicio ? 25 : 0),
        ];
        Assert.Equal(esperado, EmpresaService.ClassificarAlerta(score, resultados, O));
    }
}

public class FontesComplementaresTests
{
    private static ContextoAvaliacao Ctx(ResultadoSancoes? s = null, ResultadoPgfn? p = null, ResultadoDataJud? d = null) =>
        new(new EmpresaReceita("x", "EMPRESA TESTE LTDA", null, new(2026, 1, 1), 1, "ME", null, "Ativa", "1", "desc", [],
                null, null, null, null, "Salvador", "BA", []),
            new AutorizacaoBacen(false, null, null, null, "2026-09-30"),
            new ResultadoRanking(false, null, "2º trimestre de 2026", 100),
            new(2026, 9, 30), s, p, d);

    [Fact]
    public void Indicador07_positivo_em_qualquer_lista_e_indicio_juridico()
    {
        var ceis = new SancaoCeisCnep().Avaliar(Ctx(new ResultadoSancoes(true, null, null)));
        var cnep = new SancaoCeisCnep().Avaliar(Ctx(new ResultadoSancoes(false, true, null)));
        var nada = new SancaoCeisCnep().Avaliar(Ctx(new ResultadoSancoes(false, false, null)));
        var nulo = new SancaoCeisCnep().Avaliar(Ctx());
        Assert.Equal((25, 25, 0, 0), (ceis.ScoreObtido, cnep.ScoreObtido, nada.ScoreObtido, nulo.ScoreObtido));
        Assert.Equal(TiposIndicador.IndicioJuridico, ceis.Tipo);
        Assert.Contains("NÃO AVALIADO", nulo.Fundamentacao);
    }

    [Fact]
    public void Indicador08_divida_positiva_pontua_e_e_fator_de_risco()
    {
        var r = new DividaAtivaPgfn().Avaliar(Ctx(p: new ResultadoPgfn(true, 1_000m)));
        Assert.Equal(15, r.ScoreObtido);
        Assert.Equal(TiposIndicador.FatorRisco, r.Tipo);
        Assert.Equal(0, new DividaAtivaPgfn().Avaliar(Ctx(p: new ResultadoPgfn(false, 0m))).ScoreObtido);
        Assert.Equal(0, new DividaAtivaPgfn().Avaliar(Ctx()).ScoreObtido);
    }

    [Fact]
    public void Indicador09_processo_criminal_de_socio()
    {
        var r = new SocioProcessoCriminal().Avaliar(Ctx(d: new ResultadoDataJud(true, "FULANO", 2, 1)));
        Assert.Equal(20, r.ScoreObtido);
        Assert.Equal(TiposIndicador.IndicioJuridico, r.Tipo);
        Assert.Contains("FULANO", r.Fundamentacao);
        Assert.Equal(0, new SocioProcessoCriminal().Avaliar(Ctx()).ScoreObtido);
    }

    [Theory]
    [InlineData("***293137**", null)]
    [InlineData("123.456.789-09", "12345678909")]
    [InlineData("12345678000199", null)]
    [InlineData(null, null)]
    public void Cpf_mascarado_nao_e_consultavel(string? doc, string? esperado) =>
        Assert.Equal(esperado, FontesJson.CpfCompleto(doc));

    [Fact]
    public async Task DataJud_com_cpf_mascarado_retorna_null_sem_chamar_a_api()
    {
        var client = new DataJudClient(new HttpClient(new FalhaHandler()),
            Microsoft.Extensions.Options.Options.Create(new FontesOptions { DataJudApiKey = "teste" }), NullLogger<DataJudClient>.Instance);
        var r = await client.ConsultarAsync([new SocioReceita("LORRANE", "Sócia", "Pessoa Física", null, null, "***293137**")], default);
        Assert.Null(r);
    }

    private class FalhaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Não deveria chamar a API.");
    }

    [Fact]
    public void Le_sancoes_da_lista_do_portal()
    {
        using var vazio = System.Text.Json.JsonDocument.Parse("[]");
        Assert.Equal((false, null), FontesJson.LerSancoes(vazio.RootElement, "CEIS"));

        using var doc = System.Text.Json.JsonDocument.Parse(
            """[{"tipoSancao":{"descricaoResumida":"Inidoneidade"},"orgaoSancionador":{"nome":"CGU"},"dataInicioSancao":"01/02/2026"}]""");
        var (ativa, desc) = FontesJson.LerSancoes(doc.RootElement, "CEIS");
        Assert.True(ativa);
        Assert.Equal("CEIS: Inidoneidade – CGU (início 01/02/2026)", desc);
    }

    [Fact]
    public void Le_total_da_divida_pgfn()
    {
        using var obj = System.Text.Json.JsonDocument.Parse("""{"totalDivida": 1500.5}""");
        using var lista = System.Text.Json.JsonDocument.Parse("""[{"totalDivida": 10}, {"totalDivida": "20.5"}]""");
        Assert.Equal(1500.5m, FontesJson.LerTotalDivida(obj.RootElement));
        Assert.Equal(30.5m, FontesJson.LerTotalDivida(lista.RootElement));
    }

    [Fact]
    public void Conta_processos_com_classe_ou_assunto_criminal()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"hits":{"hits":[
              {"_source":{"classe":{"nome":"Ação Penal - Procedimento Ordinário"},"assuntos":[{"nome":"Estelionato"}]}},
              {"_source":{"classeProcessual":"Procedimento Comum Cível","assuntoProcessual":"Fraude à execução"}},
              {"_source":{"classe":{"nome":"Execução Fiscal"},"assuntos":[[{"nome":"Dívida Ativa"}]]}},
              {"_source":{"classe":{"nome":"Inquérito Policial"},"assuntos":[{"nome":"Crimes contra o Sistema Financeiro"}]}}
            ]}}
            """);
        Assert.Equal(3, FontesJson.ContarProcessosCriminais(doc.RootElement));
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
