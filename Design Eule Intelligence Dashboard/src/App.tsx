import { FormEvent, ReactNode, useState } from "react";
import { fetchEmpresa } from "./api";
import { CompanyData, Indicador, RecentSearch, Risk } from "./fixtures";

type Screen = "search" | "analysis" | "report";

const riskNotes: Record<Risk, string> = {
  ALTO: "Risco elevado. Recomenda-se análise aprofundada antes do prosseguimento.",
  MÉDIO: "Risco moderado. Recomenda-se acompanhamento e verificação complementar.",
  BAIXO: "Risco baixo. Nenhuma providência adicional recomendada neste momento.",
};

function indicatorLevel(indicador: Indicador) {
  const ratio = indicador.score / indicador.peso;
  if (ratio >= 0.66) return "high";
  if (ratio >= 0.33) return "medium";
  return "low";
}

function companyInitials(name: string) {
  return name
    .split(" ")
    .filter((word) => word.length > 2)
    .slice(0, 2)
    .map((word) => word[0])
    .join("")
    .toUpperCase();
}

function formatTimestamp(date: Date) {
  return date.toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" }).replace(",", " às");
}

function dossierNumber(company: CompanyData) {
  return company.cnpj.replace(/\D/g, "").slice(0, 8);
}

function OwlLogo() {
  return (
    <div className="brand-mark" aria-hidden="true">
      <svg viewBox="0 0 40 40" fill="none">
        <path
          d="M8.5 13.5 13 7l7 4 7-4 4.5 6.5v10.25C31.5 30.52 26.35 35 20 35S8.5 30.52 8.5 23.75V13.5Z"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinejoin="round"
        />
        <path d="M12 15.5h16M20 11v12" stroke="currentColor" strokeWidth="2" />
        <circle cx="15.5" cy="20" r="3.5" stroke="currentColor" strokeWidth="2" />
        <circle cx="24.5" cy="20" r="3.5" stroke="currentColor" strokeWidth="2" />
        <circle cx="15.5" cy="20" r="1" fill="currentColor" />
        <circle cx="24.5" cy="20" r="1" fill="currentColor" />
        <path d="m17.5 24.5 2.5 2 2.5-2M14 30c1.7 1.15 3.7 1.75 6 1.75S24.3 31.15 26 30" stroke="currentColor" strokeWidth="1.5" />
        <path d="M8.5 14 5.75 11.5M31.5 14l2.75-2.5" stroke="currentColor" strokeWidth="1.5" />
      </svg>
    </div>
  );
}

function Icon({
  children,
  viewBox = "0 0 24 24",
}: {
  children: ReactNode;
  viewBox?: string;
}) {
  return (
    <svg
      className="icon"
      viewBox={viewBox}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {children}
    </svg>
  );
}

function Header({
  screen,
  setScreen,
}: {
  screen: Screen;
  setScreen: (screen: Screen) => void;
}) {
  return (
    <header className="app-header">
      <button className="brand" onClick={() => setScreen("search")} type="button">
        <OwlLogo />
        <span className="brand-copy">
          <strong>EULE</strong>
          <small>Inteligência Institucional</small>
        </span>
      </button>
      <nav className="tabs" aria-label="Navegação principal">
        <button
          className={screen === "search" ? "tab active" : "tab"}
          onClick={() => setScreen("search")}
          type="button"
        >
          <Icon>
            <circle cx="11" cy="11" r="6.5" />
            <path d="m16 16 4 4" />
          </Icon>
          Consulta
        </button>
        <button
          className={screen === "analysis" ? "tab active" : "tab"}
          onClick={() => setScreen("analysis")}
          type="button"
        >
          <Icon>
            <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />
          </Icon>
          Análise de risco
        </button>
        <button
          className={screen === "report" ? "tab active" : "tab"}
          onClick={() => setScreen("report")}
          type="button"
        >
          <Icon>
            <path d="M6 2h8l4 4v16H6z" />
            <path d="M14 2v5h5M9 12h6M9 16h6" />
          </Icon>
          Relatório
        </button>
      </nav>
      <div className="user-area">
        <span className="environment"><i /> AMBIENTE SEGURO</span>
        <div className="avatar">MP</div>
      </div>
    </header>
  );
}

function PageTitle({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string;
  title: string;
  description: string;
}) {
  return (
    <div className="page-title">
      <span className="eyebrow">{eyebrow}</span>
      <h1>{title}</h1>
      <p>{description}</p>
    </div>
  );
}

function RiskBadge({ risk }: { risk: Risk }) {
  return <span className={`risk-badge ${risk.toLowerCase().replace("é", "e")}`}>{risk}</span>;
}

function EmptyState({ onSearch }: { onSearch: () => void }) {
  return (
    <main className="screen">
      <section className="panel empty-state">
        <span className="eyebrow">NENHUMA EMPRESA CONSULTADA</span>
        <p>Realize uma consulta por CNPJ para visualizar esta tela.</p>
        <button className="primary-button compact" onClick={onSearch} type="button">
          Ir para consulta
        </button>
      </section>
    </main>
  );
}

function SearchScreen({
  onLoad,
  recentSearches,
}: {
  onLoad: (company: CompanyData) => void;
  recentSearches: RecentSearch[];
}) {
  const [cnpj, setCnpj] = useState("");
  const [error, setError] = useState("");
  const [isLoading, setIsLoading] = useState(false);

  function handleSearch(event: FormEvent) {
    event.preventDefault();
    return consultar(cnpj);
  }

  async function consultar(alvo: string) {
    setError("");
    setIsLoading(true);
    try {
      onLoad(await fetchEmpresa(alvo));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Erro inesperado ao consultar o CNPJ.");
    } finally {
      setIsLoading(false);
    }
  }

  return (
    <main className="screen search-screen">
      <section className="search-hero">
        <div className="search-primary">
          <PageTitle
            eyebrow="CONSULTA DE PESSOA JURÍDICA"
            title="Análise cadastral e de risco"
            description="Consulte dados integrados e indicadores de risco a partir do CNPJ."
          />
          <form className="search-form" onSubmit={handleSearch}>
            <label htmlFor="cnpj">CNPJ</label>
            <div className="search-row">
              <div className="input-wrap">
                <Icon>
                  <path d="M4 21V5l8-3 8 3v16M8 9h1M8 13h1M8 17h1M15 9h1M15 13h1M15 17h1" />
                </Icon>
                <input
                  id="cnpj"
                  value={cnpj}
                  onChange={(event) => {
                    setCnpj(event.target.value);
                    setError("");
                  }}
                  placeholder="00.000.000/0000-00"
                  inputMode="numeric"
                  aria-describedby={error ? "search-error" : "search-hint"}
                  aria-invalid={error ? true : undefined}
                />
              </div>
              <button className="primary-button" type="submit" disabled={isLoading}>
                <Icon>
                  <circle cx="11" cy="11" r="6.5" />
                  <path d="m16 16 4 4" />
                </Icon>
                {isLoading ? "Consultando..." : "Consultar"}
              </button>
            </div>
            {error ? (
              <p id="search-error" className="search-error" role="alert">{error}</p>
            ) : (
              <p id="search-hint">Informe os 14 dígitos do Cadastro Nacional da Pessoa Jurídica.</p>
            )}
          </form>
        </div>
        <aside className="system-stats" aria-label="Estatísticas do sistema">
          <div className="stats-heading">
            <span>SISTEMA EM TEMPO REAL</span>
            <i />
          </div>
          <div className="stat-item">
            <span>Consultas nesta sessão</span>
            <strong>{recentSearches.length}</strong>
          </div>
          <div className="stat-item alert-stat">
            <span>Alertas de risco ALTO</span>
            <strong>{recentSearches.filter((item) => item.company.classificacao === "ALTO").length}</strong>
          </div>
          <div className="stat-update">
            <Icon><path d="M20 12a8 8 0 1 1-2.35-5.65M20 4v6h-6" /></Icon>
            <span>Última consulta<strong>{recentSearches[0]?.date ?? "Nenhuma ainda"}</strong></span>
          </div>
        </aside>
      </section>

      <section className="panel recent-panel">
        <div className="section-heading">
          <div>
            <span className="eyebrow">HISTÓRICO</span>
            <h2>Consultas recentes</h2>
          </div>
          <button className="text-button" type="button">
            Ver histórico completo
            <Icon>
              <path d="m9 18 6-6-6-6" />
            </Icon>
          </button>
        </div>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>CNPJ</th>
                <th>Razão social</th>
                <th>Data da consulta</th>
                <th>Classificação</th>
                <th aria-label="Ações" />
              </tr>
            </thead>
            <tbody>
              {recentSearches.length === 0 && (
                <tr>
                  <td colSpan={5}>Nenhuma consulta realizada nesta sessão.</td>
                </tr>
              )}
              {recentSearches.map(({ company, date }) => (
                <tr key={company.cnpj}>
                  <td className="mono">{company.cnpj}</td>
                  <td className="company-name">{company.razaoSocial}</td>
                  <td>{date}</td>
                  <td><RiskBadge risk={company.classificacao} /></td>
                  <td>
                    <button className="row-action" onClick={() => consultar(company.cnpj)} disabled={isLoading} type="button" aria-label={`Abrir análise de ${company.razaoSocial}`}>
                      <Icon><path d="m9 18 6-6-6-6" /></Icon>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </main>
  );
}

function AnalysisScreen({
  company,
  consultedAt,
  onReport,
}: {
  company: CompanyData;
  consultedAt: Date;
  onReport: () => void;
}) {
  const gaugeOffset = 603 * (1 - company.scoreTotal / 100);
  const details = [
    ["Nome fantasia", company.nomeFantasia],
    ["Data de abertura", company.dataAbertura],
    ["Atividade declarada (CNAE)", `${company.cnae} — ${company.cnaeDescricao}`],
    ["Porte", company.porte],
    ["Capital social", company.capitalSocial],
    ["Sede", `${company.municipio}/${company.uf}`],
  ];

  return (
    <main className="screen analysis-screen">
      <div className="analysis-heading">
        <PageTitle
          eyebrow={`DOSSIÊ ${dossierNumber(company)}`}
          title="Análise de risco"
          description="Consolidação de sinais cadastrais, societários e regulatórios."
        />
        <button className="secondary-button" onClick={onReport} type="button">
          <Icon><path d="M6 2h8l4 4v16H6zM14 2v5h5" /></Icon>
          Gerar relatório
        </button>
      </div>

      <div className="analysis-grid">
        <section className="panel identity-card">
          <div className="section-kicker">
            <span>IDENTIFICAÇÃO</span>
            <span className="status-dot">{company.situacaoCadastral.toUpperCase()}</span>
          </div>
          <div className="company-emblem">{companyInitials(company.razaoSocial)}</div>
          <h2>{company.razaoSocial}</h2>
          <p className="mono prominent">{company.cnpj}</p>
          <dl>
            {details.map(([term, value]) => (
              <div key={term}>
                <dt>{term}</dt>
                <dd>{value}</dd>
              </div>
            ))}
            <div>
              <dt>{company.socios.length > 1 ? "Sócios" : "Sócio"}</dt>
              {company.socios.map((socio) => (
                <dd key={socio.nome}>
                  {socio.nome} ({socio.qualificacao})
                  {socio.cnpjsAtivos !== undefined && ` · ${socio.cnpjsAtivos} CNPJs ativos vinculados`}
                </dd>
              ))}
            </div>
            <div>
              <dt>Cadastro BACEN</dt>
              <dd className={company.apareceBacen ? "success-text" : undefined}>
                {company.apareceBacen ? "Consta" : "Não consta"}
              </dd>
            </div>
            <div>
              <dt>Ranking de Reclamações</dt>
              <dd>{company.aparecRanking ? "Consta" : "Não consta"}</dd>
            </div>
          </dl>
        </section>

        <section className="panel score-card">
          <div className="section-kicker"><span>SCORE CONSOLIDADO</span><span>0—100</span></div>
          <div className="gauge">
            <svg viewBox="0 0 240 240" aria-label={`Score de risco ${company.scoreTotal} de 100`}>
              <circle className="gauge-track" cx="120" cy="120" r="96" />
              <circle className={`gauge-value ${company.classificacao.toLowerCase().replace("é", "e")}`} cx="120" cy="120" r="96" style={{ strokeDashoffset: gaugeOffset }} />
            </svg>
            <div className="gauge-copy">
              <strong>{company.scoreTotal}</strong>
              <span>/ 100</span>
            </div>
          </div>
          <RiskBadge risk={company.classificacao} />
          <p className="score-note">{riskNotes[company.classificacao]}</p>
        </section>

        <section className="panel indicators-card">
          <div className="section-kicker"><span>INDICADORES EULE</span><span>{company.indicadores.length} SINAIS</span></div>
          <div className="indicator-list">
            {company.indicadores.map((indicador) => (
              <div className={indicador.peso >= 25 ? "indicator primary" : "indicator"} key={indicador.id} title={indicador.observado}>
                <div className="indicator-meta">
                  <span><i>{indicador.id}</i>{indicador.label}<em>máx. {indicador.peso}</em></span>
                  <strong>{indicador.score}</strong>
                </div>
                <div className="progress-track">
                  <div
                    className={`progress-value ${indicatorLevel(indicador)}`}
                    style={{ width: `${(indicador.score / indicador.peso) * 100}%` }}
                  />
                </div>
              </div>
            ))}
          </div>
        </section>
      </div>

      <div className="source-bar">
        <Icon><path d="M12 22s8-3 8-10V5l-8-3-8 3v7c0 7 8 10 8 10Z" /><path d="m9 12 2 2 4-5" /></Icon>
        <span><strong>Fontes consultadas:</strong> Receita Federal (cadastro de CNPJ), Cadastro de instituições autorizadas do BACEN e Ranking de Reclamações do BACEN.</span>
        <span>Consultado em {formatTimestamp(consultedAt)}</span>
      </div>
    </main>
  );
}

const MAX_SOCIOS = 10;
const MAX_OBSERVADO = 300;

function ObservadoCell({ texto }: { texto: string }) {
  const [expandido, setExpandido] = useState(false);
  if (texto.length <= MAX_OBSERVADO) return <>{texto}</>;

  return (
    <>
      {expandido ? texto : `${texto.slice(0, MAX_OBSERVADO).trimEnd()}…`}{" "}
      <button className="ver-completo" onClick={() => setExpandido(!expandido)} type="button">
        {expandido ? "Ver menos" : "Ver completo"}
      </button>
    </>
  );
}

const recommendations = [
  "Instaurar PIC com base nos indícios levantados.",
  "Solicitar RIF ao COAF após instauração do PIC.",
  "Encaminhar representação ao BCB pela operação irregular.",
  "Aprofundar investigação societária da cadeia de sócios.",
  "Verificar enquadramento na Resolução Conjunta nº 16/2025.",
];

function ReportScreen({ company, consultedAt }: { company: CompanyData; consultedAt: Date }) {
  const reference = `EULE-${consultedAt.getFullYear()}-${dossierNumber(company)}`;

  return (
    <main className="screen report-screen">
      <div className="report-toolbar">
        <div>
          <span className="eyebrow">RELATÓRIO DE INTELIGÊNCIA</span>
          <span className="toolbar-state"><i /> Documento concluído</span>
        </div>
        <div className="toolbar-actions">
          <button className="secondary-button" type="button">
            <Icon><path d="M12 3v12M7 10l5 5 5-5M5 21h14" /></Icon>
            Exportar PDF
          </button>
          <button className="primary-button compact" type="button">
            <Icon><path d="M18 8a3 3 0 1 0-2.8-4M6 14a3 3 0 1 0 0 6M18 14a3 3 0 1 0 0 6M8.7 17.5l6.6-3M8.7 6.5l6.6 3" /></Icon>
            Compartilhar
          </button>
        </div>
      </div>

      <article className="report-document">
        <header className="report-header">
          <div className="report-brand">
            <OwlLogo />
            <div><strong>EULE</strong><span>MINISTÉRIO PÚBLICO · INTELIGÊNCIA INSTITUCIONAL</span></div>
          </div>
          <div className="report-meta">
            <span>GERADO EM</span><strong>{formatTimestamp(consultedAt).toUpperCase()}</strong>
            <span>REFERÊNCIA</span><strong>{reference}</strong>
          </div>
        </header>

        <div className="classification">USO RESTRITO · INFORMAÇÃO DE INTELIGÊNCIA</div>
        <div className="report-title">
          <span>RELATÓRIO DE ANÁLISE PRÉ-INVESTIGATIVA</span>
          <h1>{company.razaoSocial}</h1>
          <p>CNPJ {company.cnpj} · {company.municipio}/{company.uf} · Abertura em {company.dataAbertura}</p>
        </div>

        <section className="executive-summary">
          <div>
            <span className="report-label">01 · SUMÁRIO EXECUTIVO</span>
            <h2>Classificação de risco {company.classificacao.toLowerCase()}</h2>
            <p>
              Empresa de porte {company.porte.toLowerCase()}, capital social de {company.capitalSocial}, com CNAE {company.cnae} ({company.cnaeDescricao}).
            </p>
            <p>{company.socios.length > 1 ? "Sócios:" : "Sócio:"}</p>
            <ul className="socios-list">
              {company.socios.slice(0, MAX_SOCIOS).map((socio) => (
                <li key={socio.nome}>{socio.nome} ({socio.qualificacao})</li>
              ))}
              {company.socios.length > MAX_SOCIOS && <li>e mais {company.socios.length - MAX_SOCIOS} sócios</li>}
            </ul>
            <p>
              {company.apareceBacen ? "Consta" : "Não consta"} no Cadastro BACEN e {company.aparecRanking ? "consta" : "não consta"} no Ranking de Reclamações.
            </p>
          </div>
          <div className="summary-score"><strong>{company.scoreTotal}</strong><span>RISCO {company.classificacao}</span></div>
        </section>

        <section className="report-section">
          <span className="report-label">02 · MATRIZ DE INDICADORES</span>
          <div className="table-scroll">
            <table className="report-table">
              <thead><tr><th>Indicador analisado</th><th>Peso máx.</th><th>Score</th><th>Valor observado</th></tr></thead>
              <tbody>
                {company.indicadores.map((indicador) => (
                  <tr key={indicador.id}>
                    <td>{indicador.id} · {indicador.label}</td>
                    <td>{indicador.peso}</td>
                    <td className="score-cell">{indicador.score}</td>
                    <td><ObservadoCell texto={indicador.observado} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <div className="report-columns">
          <section className="report-section legal-section">
            <span className="report-label">03 · FUNDAMENTAÇÃO JURÍDICA</span>
            <h2>STF · Tema 1404 (RE 1.537.165)</h2>
            <p>A presente análise observa a tese de repercussão geral fixada pelo Supremo Tribunal Federal no Tema 1404 (RE 1.537.165), respeitando os limites de acesso, tratamento e compartilhamento de dados por órgãos de persecução.</p>
            <p>Considera-se ainda a Resolução Conjunta nº 16/2025 do BCB/CMN, que proíbe a utilização de contas-bolsão, com prazo até 31/12/2026.</p>
            <blockquote>O uso de informações deve preservar a finalidade pública, a rastreabilidade da consulta e a proporcionalidade da medida investigativa.</blockquote>
          </section>

          <section className="report-section recommendations">
            <span className="report-label">04 · RECOMENDAÇÕES</span>
            <ol>
              {recommendations.map((item, index) => (
                <li key={item}><span>{String(index + 1).padStart(2, "0")}</span>{item}</li>
              ))}
            </ol>
          </section>
        </div>

        <footer className="report-footer">
          <p>Este documento é produto de inteligência e não constitui, isoladamente, prova de ilícito. As informações devem ser corroboradas por diligências próprias antes de qualquer medida administrativa ou judicial.</p>
          <span>EULE · {reference} · PÁGINA 1/1</span>
        </footer>
      </article>
    </main>
  );
}

export default function App() {
  const [screen, setScreen] = useState<Screen>("search");
  const [company, setCompany] = useState<CompanyData | null>(null);
  const [consultedAt, setConsultedAt] = useState(() => new Date());
  const [recentSearches, setRecentSearches] = useState<RecentSearch[]>([]);

  function loadCompany(data: CompanyData) {
    const agora = new Date();
    setCompany(data);
    setConsultedAt(agora);
    setRecentSearches((anteriores) =>
      [{ company: data, date: formatTimestamp(agora) }, ...anteriores.filter((item) => item.company.cnpj !== data.cnpj)].slice(0, 5),
    );
    setScreen("analysis");
  }

  return (
    <div className="app-shell">
      <Header screen={screen} setScreen={setScreen} />
      {screen === "search" && <SearchScreen onLoad={loadCompany} recentSearches={recentSearches} />}
      {screen === "analysis" &&
        (company ? (
          <AnalysisScreen company={company} consultedAt={consultedAt} onReport={() => setScreen("report")} />
        ) : (
          <EmptyState onSearch={() => setScreen("search")} />
        ))}
      {screen === "report" &&
        (company ? (
          <ReportScreen company={company} consultedAt={consultedAt} />
        ) : (
          <EmptyState onSearch={() => setScreen("search")} />
        ))}
    </div>
  );
}
