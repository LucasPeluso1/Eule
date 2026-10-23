namespace EuleApi.Options;

public class FontesOptions
{
    public const string Section = "Fontes";

    public string ReceitaBaseUrl { get; set; } = "https://publica.cnpj.ws/cnpj/";
    public string BacenOlindaBaseUrl { get; set; } = "https://olinda.bcb.gov.br/olinda/servico/BcBase/versao/v2/odata/";
    public string BacenRankingBaseUrl { get; set; } = "https://www3.bcb.gov.br/rdrweb/rest/ext/ranking/";
    public string RankingTipo { get; set; } = "Bancos e financeiras";

    public string CguBaseUrl { get; set; } = "https://portaldatransparencia.gov.br/api-de-dados/";
    public string CguChaveApi { get; set; } = "demo";
    public string PgfnUrl { get; set; } = "https://www.listadevedores.pgfn.gov.br/api/listaDevedor";
    public string DataJudUrl { get; set; } = "https://api-publica.datajud.cnj.jus.br/api_publica_tjba/_search";

    /// <summary>
    /// Chave da API pública do DataJud (divulgada pelo CNJ). Não fica no repositório: configure com
    /// <c>dotnet user-secrets set "Fontes:DataJudApiKey" "..."</c> ou a variável <c>Fontes__DataJudApiKey</c>.
    /// Sem chave, o indicador 09 retorna null.
    /// </summary>
    public string DataJudApiKey { get; set; } = "";
    public int DataJudMaxSocios { get; set; } = 3;
}

public class CacheOptions
{
    public const string Section = "Cache";

    public int TtlHoras { get; set; } = 24;
}

public class CorsOptions
{
    public const string Section = "Cors";
    public const string PolicyName = "Frontend";

    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];
}

public class ScoreOptions
{
    public const string Section = "Score";

    /// <summary>Capital social (R$) a partir do qual o capital deixa de ser considerado incompatível.</summary>
    public decimal CapitalReferencia { get; set; } = 1_000_000m;

    /// <summary>Idade (meses) a partir da qual a constituição deixa de ser considerada recente.</summary>
    public int JanelaConstituicaoMeses { get; set; } = 60;

    /// <summary>Empresas com menos meses que isso têm a ausência no ranking atenuada (lag trimestral).</summary>
    public int EmpresaNovaMeses { get; set; } = 3;

    // Nível de risco: BAIXO < 40 ≤ MEDIO < 70 ≤ ALTO < 90 ≤ CRITICO.
    public int LimiarCritico { get; set; } = 90;
    public int LimiarAlto { get; set; } = 70;
    public int LimiarMedio { get; set; } = 40;

    // Tipo de alerta geral. Os dois primeiros exigem ao menos um INDICIO_JURIDICO positivo.
    public int LimiarFundamentoPic { get; set; } = 90;
    public int LimiarSuspeitaQualificada { get; set; } = 80;
    public int LimiarRiscoElevado { get; set; } = 60;
}
