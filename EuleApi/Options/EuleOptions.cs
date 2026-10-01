namespace EuleApi.Options;

public class FontesOptions
{
    public const string Section = "Fontes";

    public string ReceitaBaseUrl { get; set; } = "https://publica.cnpj.ws/cnpj/";
    public string BacenOlindaBaseUrl { get; set; } = "https://olinda.bcb.gov.br/olinda/servico/BcBase/versao/v2/odata/";
    public string BacenRankingBaseUrl { get; set; } = "https://www3.bcb.gov.br/rdrweb/rest/ext/ranking/";
    public string RankingTipo { get; set; } = "Bancos e financeiras";
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

    public int LimiarAlto { get; set; } = 60;
    public int LimiarMedio { get; set; } = 35;
}
