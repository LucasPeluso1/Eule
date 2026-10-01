using EuleApi.Models;
using EuleApi.Options;
using Microsoft.Extensions.Options;

namespace EuleApi.Indicadores;

public interface IIndicador
{
    IndicadorResultado Avaliar(ContextoAvaliacao ctx);
}

internal static class Meses
{
    public static int Entre(DateOnly de, DateOnly ate)
    {
        if (de == default) return int.MaxValue;
        var m = (ate.Year - de.Year) * 12 + ate.Month - de.Month;
        if (ate.Day < de.Day) m--;
        return Math.Max(m, 0);
    }
}

/// <summary>01 · Constituição recente com capital social incompatível (peso 25).</summary>
public class ConstituicaoRecenteCapitalIncompativel(IOptions<ScoreOptions> options) : IIndicador
{
    public const int Peso = 25;

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx)
    {
        var o = options.Value;
        var meses = Meses.Entre(ctx.Empresa.DataAbertura, ctx.Hoje);

        // Fator de idade: 1 até 6 meses, decaindo linearmente até 0 na janela configurada.
        var fatorIdade = meses <= 6 ? 1.0 : Math.Max(0.0, 1.0 - (meses - 6.0) / Math.Max(1, o.JanelaConstituicaoMeses - 6));
        // Fator de capital: 1 com capital zero, 0 com capital igual/superior ao de referência.
        var fatorCapital = 1.0 - Math.Min(1.0, (double)(ctx.Empresa.CapitalSocial / o.CapitalReferencia));

        var score = (int)Math.Round(Peso * fatorIdade * (0.6 + 0.4 * fatorCapital), MidpointRounding.AwayFromZero);

        return new IndicadorResultado
        {
            Codigo = "01",
            Descricao = "Constituição recente com capital social incompatível",
            PesoMaximo = Peso,
            ScoreObtido = score,
            Fundamentacao =
                $"Empresa aberta em {ctx.Empresa.DataAbertura:dd/MM/yyyy} ({meses} meses) com capital social de R$ {ctx.Empresa.CapitalSocial:N2}. " +
                $"Fórmula v1.0.0: {Peso} × fator de idade ({fatorIdade:0.00}; máximo até 6 meses, zero a partir de {o.JanelaConstituicaoMeses} meses) " +
                $"× (0,6 + 0,4 × fator de capital ({fatorCapital:0.00}); referência R$ {o.CapitalReferencia:N0}). Limiares configuráveis.",
            FonteConsultada = "Receita Federal (publica.cnpj.ws)",
        };
    }
}

/// <summary>02 · Ausência de autorização regulatória BACEN (peso 25).</summary>
public class AusenciaAutorizacaoBacen : IIndicador
{
    public const int Peso = 25;

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx)
    {
        var a = ctx.Autorizacao;
        return new IndicadorResultado
        {
            Codigo = "02",
            Descricao = "Ausência de autorização regulatória BACEN",
            PesoMaximo = Peso,
            ScoreObtido = a.Autorizado ? 0 : Peso,
            Fundamentacao = a.Autorizado
                ? $"Consta no cadastro de entidades supervisionadas do BACEN: {a.Nome} ({a.Tipo}; {a.Situacao}), data-base {a.DataBase}."
                : a.Situacao is not null
                    ? $"Consta no cadastro do BACEN, mas sem situação de autorização ativa: {a.Situacao} (data-base {a.DataBase})."
                    : $"Não consta no cadastro de entidades supervisionadas do BACEN (data-base {a.DataBase}, consulta pelo CNPJ raiz).",
            FonteConsultada = "BACEN – Entidades Supervisionadas (Olinda BcBase v2)",
        };
    }
}

/// <summary>03 · Inconsistência de objeto social, CNAE vs atividade real (peso 20).</summary>
public class InconsistenciaCnae : IIndicador
{
    public const int Peso = 20;
    private static readonly string[] GruposCompativeis = ["6492", "6619"];

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx)
    {
        var codigo = new string(ctx.Empresa.CnaeCodigo.Where(char.IsDigit).ToArray());
        var grupo = codigo.Length >= 4 ? codigo[..4] : codigo;
        var compativel = GruposCompativeis.Contains(grupo);

        return new IndicadorResultado
        {
            Codigo = "03",
            Descricao = "Inconsistência de objeto social (CNAE vs atividade real)",
            PesoMaximo = Peso,
            ScoreObtido = compativel ? 0 : Peso,
            Fundamentacao =
                $"CNAE principal {codigo} ({ctx.Empresa.CnaeDescricao}). " +
                (compativel
                    ? "Pertence aos grupos 6492/6619, compatíveis com a atividade investigada."
                    : "Fora dos grupos 6492/6619, tratado como inconsistente com intermediação de pagamentos.") +
                " Heurística v1.0.0 (comparação pelos 4 primeiros dígitos do CNAE).",
            FonteConsultada = "Receita Federal (publica.cnpj.ws)",
        };
    }
}

/// <summary>04 · Cruzamento societário atípico (peso 15). Cálculo manual nesta versão.</summary>
public class CruzamentoSocietario : IIndicador
{
    public const int Peso = 15;

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx)
    {
        var socios = ctx.Empresa.Socios.Count == 0
            ? "nenhum sócio informado pela Receita"
            : string.Join("; ", ctx.Empresa.Socios.Select(s => $"{s.Nome} ({s.Qualificacao}; doc. {s.CpfCnpjMascarado})"));

        return new IndicadorResultado
        {
            Codigo = "04",
            Descricao = "Cruzamento societário atípico",
            PesoMaximo = Peso,
            ScoreObtido = 0,
            RevisaoManual = true,
            Fundamentacao =
                $"PENDENTE DE REVISÃO MANUAL. Sócios informados pela Receita: {socios}. " +
                "O analista deve contar quantos CNPJs ativos cada sócio possui e atribuir a nota (0–15). O score total é parcial até lá.",
            FonteConsultada = "Receita Federal (publica.cnpj.ws) – quadro societário",
        };
    }
}

/// <summary>05 · Ausência no Ranking de Reclamações BACEN (peso 10).</summary>
public class AusenciaRanking(IOptions<ScoreOptions> options) : IIndicador
{
    public const int Peso = 10;
    public const int PesoAtenuado = 2;

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx)
    {
        var r = ctx.Ranking;
        var meses = Meses.Entre(ctx.Empresa.DataAbertura, ctx.Hoje);
        var nova = meses < options.Value.EmpresaNovaMeses;

        int score;
        string texto;
        if (r.Encontrado)
        {
            score = 0;
            texto = $"Consta no ranking de reclamações do BACEN ({r.Referencia}).";
        }
        else if (nova)
        {
            score = PesoAtenuado;
            texto = $"Não consta no ranking ({r.Referencia}). Nota atenuada ({PesoAtenuado} de {Peso}): empresa com {meses} meses, " +
                    "abaixo de 3 meses, e o ranking tem defasagem trimestral.";
        }
        else
        {
            score = Peso;
            texto = $"Não consta no ranking ({r.Referencia}). O ranking é trimestral e tem defasagem; a ausência não indica zero reclamações.";
        }

        return new IndicadorResultado
        {
            Codigo = "05",
            Descricao = "Ausência no Ranking de Reclamações BACEN",
            PesoMaximo = Peso,
            ScoreObtido = score,
            Fundamentacao = texto,
            FonteConsultada = "BACEN – Ranking de Reclamações (rdrweb)",
        };
    }
}

/// <summary>06 · Inconsistência geográfica, sede vs operação (peso 5). Preenchimento manual nesta versão.</summary>
public class InconsistenciaGeografica : IIndicador
{
    public const int Peso = 5;

    public IndicadorResultado Avaliar(ContextoAvaliacao ctx) => new()
    {
        Codigo = "06",
        Descricao = "Inconsistência geográfica (sede vs operação)",
        PesoMaximo = Peso,
        ScoreObtido = 0,
        RevisaoManual = true,
        Fundamentacao =
            $"PENDENTE DE REVISÃO MANUAL. Sede cadastrada em {ctx.Empresa.Municipio}/{ctx.Empresa.Uf}. " +
            "O analista deve comparar com o local de operação observado e atribuir a nota (0–5).",
        FonteConsultada = "Receita Federal (publica.cnpj.ws) – endereço",
    };
}
