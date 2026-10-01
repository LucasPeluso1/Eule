using System.Text.Json.Serialization;

namespace EuleApi.Models;

/// <summary>Resposta de GET /api/empresa/{cnpj}.</summary>
public class EmpresaResponse
{
    public required string Cnpj { get; init; }
    public required string CnpjFormatado { get; init; }
    public required string RazaoSocial { get; init; }
    public string? NomeFantasia { get; init; }
    public DateOnly DataAbertura { get; init; }
    public decimal CapitalSocial { get; init; }
    public string Cnae { get; init; } = "";
    public required string CnaeDescricao { get; init; }
    public string? Porte { get; init; }
    public string? Uf { get; init; }
    public string? Municipio { get; init; }
    public required string SituacaoCadastral { get; init; }
    public required List<SocioResponse> Socios { get; init; }

    public bool AutorizadoBacen { get; init; }
    public string? SituacaoBacen { get; init; }
    public bool ConstaNoRanking { get; init; }
    public int? PosicaoRankingReclamacoes { get; init; }
    public required string RankingObservacao { get; init; }

    public required List<IndicadorResultado> Indicadores { get; init; }
    public int ScoreTotal { get; init; }

    /// <summary>True enquanto houver indicadores de revisão manual (04 e 06) ainda não preenchidos.</summary>
    public bool ScoreParcial { get; init; }
    public required string NivelRisco { get; init; }
    public DateTime ConsultadoEm { get; init; }
    public string VersaoAlgoritmo { get; init; } = "1.0.0";
}

public class SocioResponse
{
    public required string Nome { get; init; }
    public required string Qualificacao { get; init; }
    public string? Tipo { get; init; }
    public DateOnly? DataEntrada { get; init; }
    public string? FaixaEtaria { get; init; }
    public string? CpfCnpjMascarado { get; init; }
}

public class IndicadorResultado
{
    public required string Codigo { get; init; }
    public required string Descricao { get; init; }
    public int PesoMaximo { get; init; }
    public int ScoreObtido { get; init; }
    public required string Fundamentacao { get; init; }
    public required string FonteConsultada { get; init; }

    /// <summary>True quando o valor depende de preenchimento/validação do analista.</summary>
    public bool RevisaoManual { get; init; }
}

// ---- Modelos internos (não expostos pela API) ----

public record SocioReceita(
    string Nome,
    string Qualificacao,
    string? Tipo,
    DateOnly? DataEntrada,
    string? FaixaEtaria,
    string? CpfCnpjMascarado);

public record EmpresaReceita(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    DateOnly DataAbertura,
    decimal CapitalSocial,
    string? Porte,
    string? NaturezaJuridica,
    string SituacaoCadastral,
    string CnaeCodigo,
    string CnaeDescricao,
    IReadOnlyList<string> CnaesSecundarios,
    string? Logradouro,
    string? Numero,
    string? Bairro,
    string? Cep,
    string? Municipio,
    string? Uf,
    IReadOnlyList<SocioReceita> Socios);

public record AutorizacaoBacen(bool Autorizado, string? Situacao, string? Tipo, string? Nome, string DataBase);

public record EntradaRanking(string Nome, string? Cnpj8, decimal? Indice, int? Posicao);

public record ResultadoRanking(bool Encontrado, EntradaRanking? Entrada, string Referencia, int TotalInstituicoes);

public record ContextoAvaliacao(
    EmpresaReceita Empresa,
    AutorizacaoBacen Autorizacao,
    ResultadoRanking Ranking,
    DateOnly Hoje);
