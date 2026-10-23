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

    // Indicador 07 · CEIS/CNEP (CGU). Null quando a fonte não respondeu.
    public bool? SancaoAtivaCeis { get; init; }
    public bool? SancaoAtivaCnep { get; init; }
    public string? DescricaoSancao { get; init; }

    // Indicador 08 · Dívida ativa da União (PGFN). Null quando a fonte não respondeu.
    public bool? PossuiDividaAtivaPgfn { get; init; }
    public decimal? ValorDividaPgfn { get; init; }

    // Indicador 09 · Processo criminal de sócio (DataJud/CNJ). Null quando não há CPF consultável ou a fonte falhou.
    public bool? SocioComProcessoCriminal { get; init; }
    public string? NomeSocioProcessado { get; init; }
    public int? NumeroProcessosEncontrados { get; init; }

    public required List<IndicadorResultado> Indicadores { get; init; }
    public int ScoreTotal { get; init; }

    /// <summary>True enquanto houver indicadores de revisão manual (04 e 06) ainda não preenchidos.</summary>
    public bool ScoreParcial { get; init; }
    public required string NivelRisco { get; init; }

    /// <summary>FUNDAMENTO_PIC, SUSPEITA_QUALIFICADA, RISCO_ELEVADO ou RISCO_MODERADO (ver <see cref="TiposAlerta"/>).</summary>
    public required string TipoAlertaGeral { get; init; }
    public DateTime ConsultadoEm { get; init; }
    public string VersaoAlgoritmo { get; init; } = "1.2.0";

    /// <summary>Embasamento para instauração de procedimento: fundamentação, enquadramento, diligências e proveniência.</summary>
    public required ParecerResponse Parecer { get; init; }
}

/// <summary>Embasamento do relatório, gerado a partir dos achados (ver <see cref="Services.GeradorParecer"/>).</summary>
public class ParecerResponse
{
    /// <summary>Parágrafos de fundamentação em texto corrido, prontos para uso em portaria ou despacho.</summary>
    public required List<string> Fundamentacao { get; init; }
    public required List<EnquadramentoTipico> Enquadramentos { get; init; }
    public required List<DiligenciaSugerida> Diligencias { get; init; }
    public required List<FonteConsultadaResponse> Fontes { get; init; }

    /// <summary>Limitações da análise (fontes indisponíveis, revisões pendentes, ressalvas de competência).</summary>
    public required List<string> Ressalvas { get; init; }

    /// <summary>SHA-256 dos achados (CNPJ, data/hora, indicadores e fontes), para conferir a integridade do relatório.</summary>
    public required string HashIntegridade { get; init; }
}

public class EnquadramentoTipico
{
    /// <summary>Dispositivo legal, ex.: "Lei 7.492/86, art. 16".</summary>
    public required string Dispositivo { get; init; }
    public required string Conduta { get; init; }
    /// <summary>Indicadores que sustentam a hipótese.</summary>
    public required List<string> Indicadores { get; init; }
    /// <summary>Elementos do tipo que os achados já sustentam.</summary>
    public required List<string> ElementosSustentados { get; init; }
    /// <summary>Elementos que ainda dependem de diligência.</summary>
    public required List<string> ElementosAProvar { get; init; }
    /// <summary>Justiça Estadual ou Federal.</summary>
    public required string Competencia { get; init; }
}

public class DiligenciaSugerida
{
    /// <summary>1 (prioritária) a 3.</summary>
    public int Prioridade { get; init; }
    public required string Descricao { get; init; }
    public required string Motivo { get; init; }
    public string? BaseLegal { get; init; }
    /// <summary>True quando depende de autorização judicial.</summary>
    public bool ReservaJurisdicao { get; init; }
}

public class FonteConsultadaResponse
{
    public required string Nome { get; init; }
    /// <summary>CONSULTADA, INDISPONIVEL ou NAO_CONSULTAVEL.</summary>
    public required string Situacao { get; init; }
    /// <summary>Endereço da consulta feita pelo sistema.</summary>
    public string? UrlConsulta { get; init; }
    /// <summary>Página pública em que o analista pode repetir a consulta manualmente.</summary>
    public string? UrlVerificacao { get; init; }
    public DateTime ConsultadoEm { get; init; }
    /// <summary>Data-base ou período de referência do dado, quando a fonte informa.</summary>
    public string? Referencia { get; init; }
    public required List<string> Indicadores { get; init; }
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

    /// <summary>INDICIO_JURIDICO ou FATOR_RISCO (ver <see cref="TiposIndicador"/>).</summary>
    public string Tipo { get; init; } = TiposIndicador.FatorRisco;
}

public static class TiposIndicador
{
    /// <summary>Fato com relevância jurídica própria (sanção administrativa, processo criminal).</summary>
    public const string IndicioJuridico = "INDICIO_JURIDICO";

    /// <summary>Sinal de risco que, isoladamente, não constitui indício jurídico.</summary>
    public const string FatorRisco = "FATOR_RISCO";
}

public static class TiposAlerta
{
    public const string FundamentoPic = "FUNDAMENTO_PIC";
    public const string SuspeitaQualificada = "SUSPEITA_QUALIFICADA";
    public const string RiscoElevado = "RISCO_ELEVADO";
    public const string RiscoModerado = "RISCO_MODERADO";
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

/// <summary>CEIS/CNEP: cada lista é null quando a consulta a ela falhou.</summary>
public record ResultadoSancoes(bool? Ceis, bool? Cnep, string? Descricao);

public record ResultadoPgfn(bool PossuiDivida, decimal Valor);

public record ResultadoDataJud(bool SocioComProcessoCriminal, string? NomeSocio, int NumeroProcessos, int SociosConsultados);

/// <summary>As fontes complementares (07–09) são opcionais: null significa fonte indisponível ou não consultável.</summary>
public record ContextoAvaliacao(
    EmpresaReceita Empresa,
    AutorizacaoBacen Autorizacao,
    ResultadoRanking Ranking,
    DateOnly Hoje,
    ResultadoSancoes? Sancoes = null,
    ResultadoPgfn? Pgfn = null,
    ResultadoDataJud? DataJud = null);
