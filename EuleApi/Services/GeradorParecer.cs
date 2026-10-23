using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EuleApi.Infrastructure;
using EuleApi.Models;
using EuleApi.Options;

namespace EuleApi.Services;

/// <summary>
/// Transforma os achados dos indicadores em embasamento para instauração de procedimento: fundamentação em texto
/// corrido, hipóteses de enquadramento típico, diligências sugeridas, proveniência das fontes e ressalvas.
/// Função pura: mesmos achados, mesmo parecer (e mesmo hash).
/// </summary>
public static class GeradorParecer
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static ParecerResponse Gerar(
        ContextoAvaliacao ctx,
        IReadOnlyList<IndicadorResultado> resultados,
        string tipoAlerta,
        DateTime consultadoEm,
        FontesOptions fontes)
    {
        var a = new Achados(ctx, resultados);
        var fontesConsultadas = Fontes(ctx, consultadoEm, fontes);
        var enquadramentos = Enquadramentos(a);

        return new ParecerResponse
        {
            Fundamentacao = Fundamentacao(a, enquadramentos, tipoAlerta, consultadoEm),
            Enquadramentos = enquadramentos,
            Diligencias = Diligencias(a, enquadramentos),
            Fontes = fontesConsultadas,
            Ressalvas = Ressalvas(a, enquadramentos),
            HashIntegridade = Hash(ctx.Empresa.Cnpj, consultadoEm, resultados, fontesConsultadas),
        };
    }

    /// <summary>Leitura dos resultados por código, para as regras abaixo não dependerem da ordem da lista.</summary>
    private sealed class Achados(ContextoAvaliacao ctx, IReadOnlyList<IndicadorResultado> resultados)
    {
        public ContextoAvaliacao Ctx { get; } = ctx;
        public EmpresaReceita Empresa => Ctx.Empresa;

        public bool Positivo(string codigo) => resultados.Any(r => r.Codigo == codigo && r.ScoreObtido > 0);
        public bool Pendente(string codigo) => resultados.Any(r => r.Codigo == codigo && r.RevisaoManual);

        public IEnumerable<string> IndiciosJuridicos =>
            resultados.Where(r => r.Tipo == TiposIndicador.IndicioJuridico && r.ScoreObtido > 0).Select(r => r.Codigo);

        public int Meses
        {
            get
            {
                var de = Empresa.DataAbertura;
                if (de == default) return 0;
                var m = (Ctx.Hoje.Year - de.Year) * 12 + Ctx.Hoje.Month - de.Month;
                if (Ctx.Hoje.Day < de.Day) m--;
                return Math.Max(m, 0);
            }
        }
    }

    // ---------------------------------------------------------------- Fundamentação

    private static List<string> Fundamentacao(Achados a, List<EnquadramentoTipico> enquadramentos, string tipoAlerta, DateTime consultadoEm)
    {
        var e = a.Empresa;
        var paragrafos = new List<string>
        {
            $"Trata-se de análise pré-investigativa, realizada em {consultadoEm:dd/MM/yyyy} às {consultadoEm:HH:mm} (UTC) a partir de bases públicas oficiais, " +
            $"da sociedade {e.RazaoSocial}, CNPJ {Cnpj.Formatar(e.Cnpj)}, com sede em {e.Municipio}/{e.Uf}, constituída em {e.DataAbertura:dd/MM/yyyy}, " +
            $"capital social declarado de {Moeda(e.CapitalSocial)}, CNAE principal {e.CnaeCodigo} ({e.CnaeDescricao}) e situação cadastral {e.SituacaoCadastral.ToLowerInvariant()}.",
        };

        var fatos = Fatos(a);
        paragrafos.Add(fatos.Count == 0
            ? "Nas fontes consultadas não foram identificados achados positivos."
            : "Das consultas resultaram os seguintes fatos: " + Enumerar(fatos) + ".");

        if (enquadramentos.Count > 0)
            paragrafos.Add(
                "Em tese, e sem prejuízo de outra capitulação após a instrução, os fatos podem se amoldar a: " +
                Enumerar(enquadramentos.Select(x => $"{x.Conduta} ({x.Dispositivo})").ToList()) + ". " +
                "Os achados já sustentam parte dos elementos desses tipos; os elementos restantes, em especial a existência de vítimas, " +
                "a movimentação de recursos de terceiros e a autoria, dependem das diligências indicadas neste relatório.");

        paragrafos.Add(Conclusao(a, tipoAlerta));

        paragrafos.Add(
            "Os dados acima são de fontes abertas, foram obtidos sem afastamento de sigilo e servem como informação de inteligência. " +
            "Não constituem prova isoladamente e devem ser corroborados pelas diligências próprias antes de medida que restrinja direitos.");

        return paragrafos;
    }

    private static List<string> Fatos(Achados a)
    {
        var e = a.Empresa;
        var c = a.Ctx;
        var fatos = new List<string>();

        if (a.Positivo("01"))
            fatos.Add($"a empresa foi constituída há {a.Meses} meses com capital social de {Moeda(e.CapitalSocial)}, " +
                      "valor reduzido para quem atua com recursos de terceiros, o que é compatível com o uso instrumental da pessoa jurídica");
        if (a.Positivo("02"))
            fatos.Add(c.Autorizacao.Situacao is not null
                ? $"embora conste no cadastro do Banco Central, não possui autorização ativa (situação: {c.Autorizacao.Situacao}; data-base {Data(c.Autorizacao.DataBase)})"
                : $"não consta no cadastro de entidades supervisionadas pelo Banco Central (data-base {Data(c.Autorizacao.DataBase)}), " +
                  "não detendo, portanto, autorização para funcionar como instituição financeira ou de pagamento");
        if (a.Positivo("03"))
            fatos.Add($"o objeto declarado à Receita Federal (CNAE {e.CnaeCodigo}, {e.CnaeDescricao}) não corresponde à intermediação de pagamentos, " +
                      "divergência que pode indicar ocultação da atividade efetivamente exercida");
        if (a.Positivo("04"))
            fatos.Add("o analista identificou cruzamento societário atípico entre os sócios e outras pessoas jurídicas");
        if (a.Positivo("05") && !c.Ranking.Encontrado)
            fatos.Add($"a empresa não figura no Ranking de Reclamações do Banco Central ({c.Ranking.Referencia}), o que é coerente com atuação fora do perímetro supervisionado");
        if (a.Positivo("06"))
            fatos.Add($"o analista identificou divergência entre a sede cadastrada ({e.Municipio}/{e.Uf}) e o local de operação");
        if (a.Positivo("07") && c.Sancoes is { } s)
            fatos.Add("consta sanção administrativa registrada no Portal da Transparência (CEIS/CNEP)" + (s.Descricao is null ? "" : $": {s.Descricao}"));
        if (a.Positivo("08") && c.Pgfn is { } p)
            fatos.Add($"a empresa está inscrita em dívida ativa da União no valor total de {Moeda(p.Valor)}");
        if (a.Positivo("09") && c.DataJud is { } d)
            fatos.Add($"o sócio {d.NomeSocio} figura em {d.NumeroProcessos} processo(s) de natureza criminal no TJBA, segundo o DataJud/CNJ");

        return fatos;
    }

    private static string Conclusao(Achados a, string tipoAlerta)
    {
        var indicios = a.IndiciosJuridicos.ToList();
        var codigos = indicios.Count == 0 ? "" : $" (indicador{(indicios.Count > 1 ? "es" : "")} {string.Join(", ", indicios)})";

        return tipoAlerta switch
        {
            TiposAlerta.FundamentoPic =>
                $"Há indício jurídico{codigos} somado a fatores de risco convergentes, o que configura, em juízo preliminar, justa causa " +
                "para instauração de Procedimento Investigatório Criminal (Resolução CNMP nº 181/2017) ou para requisição de inquérito policial, " +
                "com prioridade para as diligências marcadas como prioritárias.",
            TiposAlerta.SuspeitaQualificada =>
                $"Há indício jurídico{codigos}, mas os fatores de risco ainda não bastam para justa causa plena. Recomenda-se registrar Notícia de Fato " +
                "(Resolução CNMP nº 174/2017) e cumprir as diligências prioritárias, que tendem a reunir os elementos necessários à instauração de PIC.",
            TiposAlerta.RiscoElevado =>
                "Os achados são fatores de risco convergentes, sem indício jurídico autônomo. Isoladamente não configuram justa causa para PIC. " +
                "Recomenda-se registrar Notícia de Fato (Resolução CNMP nº 174/2017) e cumprir as diligências prioritárias, voltadas a obter elementos " +
                "de materialidade: vítimas, oferta pública do serviço e movimentação de recursos de terceiros.",
            _ =>
                "Os achados não recomendam, por ora, medida investigativa. Sugere-se manter a empresa em monitoramento e reavaliar diante de notícia de vítimas ou de novos elementos.",
        };
    }

    // ---------------------------------------------------------------- Enquadramento típico

    private static List<EnquadramentoTipico> Enquadramentos(Achados a)
    {
        var lista = new List<EnquadramentoTipico>();
        var antecedentes = Antecedentes(a);

        if (a.Positivo("02"))
        {
            lista.Add(new EnquadramentoTipico
            {
                Dispositivo = "Lei 7.492/86, art. 16",
                Conduta = "Fazer operar instituição financeira sem a devida autorização",
                Indicadores = Codigos(a, "02", "03"),
                ElementosSustentados =
                [
                    "Ausência de autorização do Banco Central (indicador 02)",
                    .. a.Positivo("03") ? ["Objeto social declarado diverso da atividade financeira (indicador 03)"] : Array.Empty<string>(),
                    .. antecedentes,
                ],
                ElementosAProvar =
                [
                    "Exercício efetivo de atividade de instituição financeira: captação, intermediação ou aplicação de recursos de terceiros (art. 1º da Lei 7.492/86)",
                    "Autoria: quem, de fato, faz a empresa operar (sócios de direito e de fato)",
                    "Dolo",
                ],
                Competencia = "Federal (art. 26 da Lei 7.492/86 e art. 109, VI, da Constituição)",
            });

            lista.Add(new EnquadramentoTipico
            {
                Dispositivo = "Lei 1.521/51, art. 2º, IX",
                Conduta = "Crime contra a economia popular (ganhos ilícitos em detrimento de número indeterminado de pessoas, como pirâmide financeira)",
                Indicadores = Codigos(a, "01", "02"),
                ElementosSustentados =
                [
                    "Captação fora do perímetro regulado (indicador 02)",
                    .. a.Positivo("01") ? ["Estrutura recente e subcapitalizada, sem lastro para os rendimentos eventualmente prometidos (indicador 01)"] : Array.Empty<string>(),
                    .. antecedentes,
                ],
                ElementosAProvar =
                [
                    "Oferta ao público de ganhos sustentados pela entrada de novos participantes ou por processo fraudulento equivalente",
                    "Número indeterminado de vítimas",
                ],
                Competencia = "Estadual (Súmula 498 do STF)",
            });
        }

        if (a.Positivo("01") || a.Positivo("02") || a.Positivo("03"))
            lista.Add(new EnquadramentoTipico
            {
                Dispositivo = "Código Penal, art. 171 (e § 2º-A, se por meio eletrônico)",
                Conduta = "Estelionato / fraude eletrônica",
                Indicadores = Codigos(a, "01", "02", "03", "05"),
                ElementosSustentados =
                [
                    .. a.Positivo("01") ? ["Pessoa jurídica recente e subcapitalizada, apta a servir de instrumento (indicador 01)"] : Array.Empty<string>(),
                    .. a.Positivo("02") ? ["Atuação em atividade financeira sem autorização, que afasta a confiança legítima do usuário (indicador 02)"] : Array.Empty<string>(),
                    .. a.Positivo("03") ? ["Divergência entre objeto declarado e atividade real, indicativa de artifício (indicador 03)"] : Array.Empty<string>(),
                    .. antecedentes,
                ],
                ElementosAProvar =
                [
                    "Vítimas identificadas e prejuízo patrimonial",
                    "Meio fraudulento (artifício ou ardil) e nexo com a vantagem obtida",
                    "Uso de rede social, contato telefônico, e-mail ou meio análogo, para a forma do § 2º-A",
                ],
                Competencia = "Estadual",
            });

        if (a.Positivo("02") && (a.Positivo("01") || a.Positivo("04")))
            lista.Add(new EnquadramentoTipico
            {
                Dispositivo = "Lei 9.613/98, art. 1º",
                Conduta = "Lavagem de dinheiro",
                Indicadores = Codigos(a, "01", "02", "04"),
                ElementosSustentados =
                [
                    .. a.Positivo("01") ? ["Pessoa jurídica recém-constituída e sem lastro patrimonial, apta a servir de interposta pessoa (indicador 01)"] : Array.Empty<string>(),
                    .. a.Positivo("04") ? ["Rede societária atípica entre os sócios (indicador 04)"] : Array.Empty<string>(),
                    "Recursos de terceiros transitando fora do sistema supervisionado (indicador 02)",
                    .. antecedentes,
                ],
                ElementosAProvar =
                [
                    "Infração penal antecedente",
                    "Movimentação financeira incompatível com o porte e o capital (RIF do COAF, afastamento de sigilo bancário)",
                    "Atos de ocultação ou dissimulação da origem dos valores",
                ],
                Competencia = "Conforme a infração antecedente (art. 2º, III, da Lei 9.613/98)",
            });

        return lista;
    }

    /// <summary>Indícios jurídicos que reforçam qualquer hipótese (dolo, autoria, reiteração).</summary>
    private static string[] Antecedentes(Achados a) =>
    [
        .. a.Positivo("07") ? ["Sanção administrativa no CEIS/CNEP, relevante para dolo e reiteração (indicador 07)"] : Array.Empty<string>(),
        .. a.Positivo("09") ? ["Sócio com processo criminal no TJBA, relevante para autoria e reiteração (indicador 09)"] : Array.Empty<string>(),
    ];

    private static List<string> Codigos(Achados a, params string[] candidatos) =>
        candidatos.Concat(["07", "09"]).Where(a.Positivo).Distinct().ToList();

    // ---------------------------------------------------------------- Diligências

    private static List<DiligenciaSugerida> Diligencias(Achados a, List<EnquadramentoTipico> enquadramentos)
    {
        var d = new List<DiligenciaSugerida>();
        var temHipotese = enquadramentos.Count > 0;

        if (temHipotese)
        {
            d.Add(new()
            {
                Prioridade = 1,
                Descricao = "Preservar as páginas, perfis e anúncios em que a empresa oferece o serviço, com captura técnica (hash) ou ata notarial, e requerer a guarda dos registros de acesso aos provedores.",
                Motivo = "Material de oferta pública é a prova mais volátil e demonstra a atividade efetivamente exercida.",
                BaseLegal = "Lei 12.965/14, art. 15, § 2º",
            });
            d.Add(new()
            {
                Prioridade = 1,
                Descricao = "Levantar reclamações de consumidores em consumidor.gov.br, Procon e Reclame Aqui, e ouvir as vítimas identificadas em termo de declarações.",
                Motivo = "Vítimas e prejuízo são os elementos que faltam para estelionato e crime contra a economia popular.",
            });
        }

        if (a.Positivo("02"))
        {
            d.Add(new()
            {
                Prioridade = 1,
                Descricao = "Oficiar ao Banco Central para confirmar a ausência de autorização e informar se há comunicação, processo ou alerta sobre a empresa.",
                Motivo = "Converte a consulta de base aberta (indicador 02) em informação oficial juntável aos autos.",
            });
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Consultar os alertas (stop orders) da CVM em nome da empresa e dos sócios.",
                Motivo = "Oferta de investimento sem registro reforça a captação irregular de recursos.",
            });
        }

        if (a.Positivo("02") && (a.Positivo("01") || a.Positivo("03")))
        {
            d.Add(new()
            {
                Prioridade = 1,
                Descricao = "Solicitar Relatório de Inteligência Financeira (RIF) ao COAF sobre a empresa e os sócios.",
                Motivo = "Mostra o volume e a origem dos recursos movimentados, que a análise de fontes abertas não alcança.",
                BaseLegal = "Lei 9.613/98, art. 15; STF, Tema 990",
            });
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Representar pelo afastamento do sigilo bancário da empresa e pela identificação das instituições e PSPs em que mantém contas.",
                Motivo = "Necessário para provar a captação de recursos de terceiros e eventual lavagem.",
                BaseLegal = "LC 105/2001, art. 1º, § 4º",
                ReservaJurisdicao = true,
            });
        }

        d.Add(new()
        {
            Prioridade = temHipotese ? 2 : 3,
            Descricao = $"Obter na Junta Comercial{(a.Empresa.Uf is { } uf ? $" ({uf})" : "")} o contrato social e todas as alterações da empresa.",
            Motivo = "Mostra o histórico de sócios, mudanças de objeto e de capital, e quem assina pela empresa.",
        });

        if (a.Pendente("04") || a.Positivo("04"))
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Levantar as demais empresas em que os sócios figuram (Junta Comercial, Redesim) e verificar se há padrão de abertura e baixa.",
                Motivo = "Completa o indicador 04, ainda dependente de revisão manual, e pode revelar sócios interpostos.",
            });

        if (a.Ctx.DataJud is null || !a.Positivo("09"))
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Pesquisar antecedentes dos sócios pelo nome nos sistemas do TJBA e dos tribunais das UFs ligadas à empresa, e obter o CPF completo por requisição à Receita Federal.",
                Motivo = "A Receita publica o CPF dos sócios mascarado, o que limita a busca automática do indicador 09.",
            });

        if (a.Positivo("07"))
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Requisitar ao órgão sancionador cópia do processo administrativo que resultou na sanção registrada no CEIS/CNEP.",
                Motivo = "O processo pode conter fatos e documentos aproveitáveis na investigação.",
            });

        if (a.Positivo("09"))
            d.Add(new()
            {
                Prioridade = 2,
                Descricao = "Obter certidão e cópia dos processos criminais do sócio apontados pelo DataJud.",
                Motivo = "Confirma a identidade do sócio e permite avaliar se os fatos se relacionam com a atividade da empresa.",
            });

        if (a.Positivo("08"))
            d.Add(new()
            {
                Prioridade = 3,
                Descricao = "Obter certidão da dívida ativa na PGFN.",
                Motivo = "Documenta a situação fiscal e pode indicar esvaziamento patrimonial.",
            });

        return d.OrderBy(x => x.Prioridade).ToList();
    }

    // ---------------------------------------------------------------- Fontes e ressalvas

    private static List<FonteConsultadaResponse> Fontes(ContextoAvaliacao ctx, DateTime agora, FontesOptions o)
    {
        var cnpj = ctx.Empresa.Cnpj;
        string Situacao(object? r) => r is null ? "INDISPONIVEL" : "CONSULTADA";

        return
        [
            new()
            {
                Nome = "Receita Federal – CNPJ (publica.cnpj.ws)",
                Situacao = "CONSULTADA",
                UrlConsulta = o.ReceitaBaseUrl.TrimEnd('/') + "/" + cnpj,
                UrlVerificacao = "https://solucoes.receita.fazenda.gov.br/Servicos/cnpjreva/Cnpjreva_Solicitacao.asp",
                ConsultadoEm = agora,
                Indicadores = ["01", "03", "04", "06"],
            },
            new()
            {
                Nome = "Banco Central – Entidades Supervisionadas (Olinda BcBase)",
                Situacao = "CONSULTADA",
                UrlConsulta = o.BacenOlindaBaseUrl.TrimEnd('/') + "/EntidadesSupervisionadas",
                UrlVerificacao = "https://www.bcb.gov.br/estabilidadefinanceira/encontreinstituicao",
                ConsultadoEm = agora,
                Referencia = $"data-base {Data(ctx.Autorizacao.DataBase)}",
                Indicadores = ["02"],
            },
            new()
            {
                Nome = "Banco Central – Ranking de Reclamações",
                Situacao = "CONSULTADA",
                UrlConsulta = o.BacenRankingBaseUrl,
                UrlVerificacao = "https://www.bcb.gov.br/meubc/rankingreclamacoes",
                ConsultadoEm = agora,
                Referencia = ctx.Ranking.Referencia,
                Indicadores = ["05"],
            },
            new()
            {
                Nome = "CGU – Portal da Transparência (CEIS e CNEP)",
                Situacao = Situacao(ctx.Sancoes),
                UrlConsulta = $"{o.CguBaseUrl.TrimEnd('/')}/ceis?cnpjSancionado={cnpj}",
                UrlVerificacao = "https://portaldatransparencia.gov.br/sancoes/consulta",
                ConsultadoEm = agora,
                Indicadores = ["07"],
            },
            new()
            {
                Nome = "PGFN – Lista de devedores",
                Situacao = Situacao(ctx.Pgfn),
                UrlConsulta = $"{o.PgfnUrl}?cnpj={cnpj}",
                UrlVerificacao = "https://www.listadevedores.pgfn.gov.br/",
                ConsultadoEm = agora,
                Indicadores = ["08"],
            },
            new()
            {
                Nome = "CNJ – DataJud (TJBA)",
                // Sem CPF consultável o cliente nem chega a consultar; null não distingue os dois casos.
                Situacao = ctx.DataJud is null ? "NAO_CONSULTAVEL" : "CONSULTADA",
                UrlConsulta = o.DataJudUrl,
                ConsultadoEm = agora,
                Referencia = ctx.DataJud is { } d ? $"{d.SociosConsultados} sócio(s) consultado(s)" : null,
                Indicadores = ["09"],
            },
        ];
    }

    private static List<string> Ressalvas(Achados a, List<EnquadramentoTipico> enquadramentos)
    {
        var r = new List<string>();
        var c = a.Ctx;

        if (c.Sancoes is null) r.Add("CEIS/CNEP indisponível no momento da consulta: o indicador 07 não foi avaliado e deve ser verificado manualmente.");
        if (c.Pgfn is null) r.Add("Lista de devedores da PGFN indisponível no momento da consulta: o indicador 08 não foi avaliado.");
        if (c.DataJud is null) r.Add("O indicador 09 não foi avaliado porque a Receita publica o CPF dos sócios mascarado ou o DataJud não respondeu.");
        if (a.Pendente("04") || a.Pendente("06")) r.Add("Os indicadores 04 e 06 dependem de revisão manual; até lá o score é parcial.");
        if (!c.Ranking.Encontrado) r.Add("O Ranking de Reclamações é trimestral e defasado; a ausência nele não significa ausência de reclamações.");
        if (enquadramentos.Any(e => e.Dispositivo.StartsWith("Lei 7.492/86")))
            r.Add("A hipótese do art. 16 da Lei 7.492/86 é de competência federal. Confirmada, cabe comunicação ao Ministério Público Federal, " +
                  "sem prejuízo da apuração, pelo MPBA, dos crimes de competência estadual.");
        r.Add("O score é ferramenta de triagem e não substitui a análise do membro do Ministério Público.");
        return r;
    }

    private static string Hash(string cnpj, DateTime consultadoEm, IReadOnlyList<IndicadorResultado> resultados, List<FonteConsultadaResponse> fontes)
    {
        var json = JsonSerializer.Serialize(new { cnpj, consultadoEm, resultados, fontes });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- Texto

    private static string Moeda(decimal v) => v.ToString("C2", PtBr);

    /// <summary>Data-base do BACEN vem em ISO (aaaa-mm-dd); no texto, dd/mm/aaaa.</summary>
    private static string Data(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy") : iso;

    /// <summary>"(i) a; (ii) b; e (iii) c".</summary>
    private static string Enumerar(IReadOnlyList<string> itens)
    {
        string[] romanos = ["i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"];
        var partes = itens.Select((t, i) => $"({romanos[Math.Min(i, romanos.Length - 1)]}) {t}").ToList();
        return partes.Count <= 1 ? string.Concat(partes) : string.Join("; ", partes[..^1]) + "; e " + partes[^1];
    }
}
