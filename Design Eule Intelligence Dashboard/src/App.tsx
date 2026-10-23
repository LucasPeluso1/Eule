import { FormEvent, ReactNode, useRef, useState } from "react";
import gsap from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";
import { useGSAP } from "@gsap/react";
import { fetchEmpresa } from "./api";
import { CompanyData, Indicador, RecentSearch, Risk } from "./fixtures";

gsap.registerPlugin(useGSAP, ScrollTrigger);

type Screen = "search" | "analysis" | "report";

const riskNotes: Record<Risk, string> = {
  ALTO: "Risco elevado. Recomenda-se análise aprofundada antes do prosseguimento.",
  MÉDIO: "Risco moderado. Recomenda-se acompanhamento e verificação complementar.",
  BAIXO: "Risco baixo. Nenhuma providência adicional recomendada neste momento.",
};

/** Circunferência do arco do gauge (r = 96). */
const GAUGE_CIRCUMFERENCE = 2 * Math.PI * 96;

const prefersReducedMotion = () =>
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function indicatorLevel(indicador: Indicador) {
  const ratio = indicador.score / indicador.peso;
  if (ratio >= 0.66) return "high";
  if (ratio >= 0.33) return "medium";
  return "low";
}

function riskClass(risk: Risk) {
  return risk.toLowerCase().replace("é", "e");
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
          <small>NUCCIBER · MPBA</small>
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
        <span className="environment"><i /> Ambiente seguro</span>
        <div className="avatar">MP</div>
      </div>
    </header>
  );
}

function RiskBadge({ risk, large = false }: { risk: Risk; large?: boolean }) {
  return (
    <span className={`risk-badge ${riskClass(risk)}${large ? " large" : ""}`}>
      <i aria-hidden="true" />
      Risco {risk}
    </span>
  );
}

function EmptyState({ onSearch }: { onSearch: () => void }) {
  return (
    <main className="screen">
      <section className="panel empty-state">
        <span className="eyebrow">Nenhuma empresa consultada</span>
        <p>Realize uma consulta por CNPJ para visualizar esta tela.</p>
        <button className="primary-button" onClick={onSearch} type="button">
          Ir para consulta
        </button>
      </section>
    </main>
  );
}

/** Estado da consulta por CNPJ, compartilhado pelos campos de busca das telas. */
function useConsulta(onLoad: (company: CompanyData) => void) {
  const [error, setError] = useState("");
  const [isLoading, setIsLoading] = useState(false);

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

  return { error, setError, isLoading, consultar };
}

function SearchField({
  consulta,
  compact = false,
}: {
  consulta: ReturnType<typeof useConsulta>;
  compact?: boolean;
}) {
  const [cnpj, setCnpj] = useState("");
  const { error, setError, isLoading, consultar } = consulta;
  const inputId = compact ? "cnpj-compact" : "cnpj";

  function handleSearch(event: FormEvent) {
    event.preventDefault();
    return consultar(cnpj);
  }

  return (
    <form className={compact ? "search-form compact" : "search-form"} onSubmit={handleSearch} role="search">
      <label htmlFor={inputId} className={compact ? "visually-hidden" : undefined}>CNPJ da empresa</label>
      <div className="search-row">
        <div className="input-wrap">
          <Icon>
            <circle cx="11" cy="11" r="6.5" />
            <path d="m16 16 4 4" />
          </Icon>
          <input
            id={inputId}
            value={cnpj}
            onChange={(event) => {
              setCnpj(event.target.value);
              setError("");
            }}
            placeholder="00.000.000/0000-00"
            inputMode="numeric"
            autoComplete="off"
            aria-describedby={error ? `${inputId}-error` : `${inputId}-hint`}
            aria-invalid={error ? true : undefined}
          />
        </div>
        <button className="primary-button" type="submit" disabled={isLoading}>
          {isLoading && <span className="spinner" aria-hidden="true" />}
          {isLoading ? "Consultando..." : "Consultar"}
        </button>
      </div>
      {error ? (
        <p id={`${inputId}-error`} className="search-error" role="alert">{error}</p>
      ) : (
        !compact && <p id={`${inputId}-hint`} className="search-hint">Informe os 14 dígitos do Cadastro Nacional da Pessoa Jurídica.</p>
      )}
    </form>
  );
}

function SearchScreen({
  onLoad,
  recentSearches,
}: {
  onLoad: (company: CompanyData) => void;
  recentSearches: RecentSearch[];
}) {
  const consulta = useConsulta(onLoad);
  const { isLoading, consultar } = consulta;
  const root = useRef<HTMLElement>(null);

  useGSAP(() => {
    if (prefersReducedMotion()) return;
    gsap.from(".reveal", { autoAlpha: 0, y: 16, duration: 0.5, ease: "power2.out", stagger: 0.08 });
  }, { scope: root });

  return (
    <main className="screen search-screen" ref={root}>
      <section className="zone zone-search">
        <span className="eyebrow reveal">Inteligência pré-investigativa</span>
        <h1 className="reveal">Consulta de pessoa jurídica</h1>
        <p className="lede reveal">
          Cruza dados públicos da Receita Federal e do Banco Central para identificar intermediadoras de pagamento
          suspeitas e apontar justa causa para PIC ou IP.
        </p>
        <div className="reveal search-holder">
          <SearchField consulta={consulta} />
        </div>
        <dl className="session-stats reveal" aria-label="Estatísticas da sessão">
          <div>
            <dt>Consultas nesta sessão</dt>
            <dd>{recentSearches.length}</dd>
          </div>
          <div className="alert-stat">
            <dt>Alertas de risco ALTO</dt>
            <dd>{recentSearches.filter((item) => item.company.classificacao === "ALTO").length}</dd>
          </div>
          <div>
            <dt>Última consulta</dt>
            <dd>{recentSearches[0]?.date ?? "Nenhuma ainda"}</dd>
          </div>
        </dl>
      </section>

      <section className="panel recent-panel reveal">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Histórico</span>
            <h2>Consultas recentes</h2>
          </div>
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
                  <td colSpan={5} className="empty-row">Nenhuma consulta realizada nesta sessão.</td>
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

function ScoreGauge({ score, risk }: { score: number; risk: Risk }) {
  const root = useRef<HTMLDivElement>(null);
  const arc = useRef<SVGCircleElement>(null);
  const value = useRef<HTMLElement>(null);

  useGSAP(() => {
    const estado = { v: 0 };
    const desenhar = () => {
      const preenchido = Math.min(Math.max(estado.v, 0), 100) / 100;
      arc.current?.style.setProperty("stroke-dashoffset", String(GAUGE_CIRCUMFERENCE * (1 - preenchido)));
      if (value.current) value.current.textContent = String(Math.round(estado.v));
    };

    if (prefersReducedMotion()) {
      estado.v = score;
      desenhar();
      return;
    }
    desenhar();
    gsap.to(estado, { v: score, duration: 0.8, ease: "power2.out", onUpdate: desenhar });
  }, { scope: root, dependencies: [score] });

  return (
    <div className="gauge" ref={root}>
      <svg viewBox="0 0 240 240" role="img" aria-label={`Score de risco ${score} de 100`}>
        <circle className="gauge-track" cx="120" cy="120" r="96" />
        <circle className="gauge-ticks" cx="120" cy="120" r="110" />
        <circle
          ref={arc}
          className={`gauge-value ${riskClass(risk)}`}
          cx="120"
          cy="120"
          r="96"
          style={{ strokeDasharray: GAUGE_CIRCUMFERENCE, strokeDashoffset: GAUGE_CIRCUMFERENCE }}
        />
      </svg>
      <div className="gauge-copy" aria-hidden="true">
        <strong ref={value}>0</strong>
        <span>de 100</span>
      </div>
    </div>
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

const levelLabels: Record<ReturnType<typeof indicatorLevel>, string> = {
  high: "Sinal forte",
  medium: "Sinal moderado",
  low: "Sem sinal relevante",
};

function IndicatorCard({ indicador }: { indicador: Indicador }) {
  const level = indicatorLevel(indicador);

  return (
    <li className={`indicator-card ${level}`}>
      <span className="status-bar" aria-hidden="true" />
      <div className="indicator-body">
        <div className="indicator-head">
          <span className="indicator-code">{indicador.id}</span>
          <h3>{indicador.label}</h3>
          <span className="indicator-score">
            <strong>{indicador.score}</strong>
            <small>/ {indicador.peso}</small>
          </span>
        </div>
        <p className="indicator-description"><ObservadoCell texto={indicador.observado} /></p>
        <div className="indicator-foot">
          <span className={`level-label ${level}`}>{levelLabels[level]}</span>
        </div>
      </div>
    </li>
  );
}

function AnalysisScreen({
  company,
  consultedAt,
  onLoad,
  onReport,
}: {
  company: CompanyData;
  consultedAt: Date;
  onLoad: (company: CompanyData) => void;
  onReport: () => void;
}) {
  const consulta = useConsulta(onLoad);
  const root = useRef<HTMLElement>(null);
  const details = [
    ["Nome fantasia", company.nomeFantasia],
    ["Data de abertura", company.dataAbertura],
    ["Atividade declarada (CNAE)", `${company.cnae} — ${company.cnaeDescricao}`],
    ["Porte", company.porte],
    ["Capital social", company.capitalSocial],
    ["Sede", `${company.municipio}/${company.uf}`],
    ["Cadastro BACEN", company.apareceBacen ? "Consta" : "Não consta"],
    ["Ranking de Reclamações", company.aparecRanking ? "Consta" : "Não consta"],
  ];

  useGSAP(() => {
    if (prefersReducedMotion()) return;
    const tl = gsap.timeline({ defaults: { ease: "power2.out" } });
    tl.from(".zone-score", { autoAlpha: 0, y: 12, duration: 0.45 })
      .from(".zone-report", { height: 0, autoAlpha: 0, duration: 0.6, clearProps: "height" }, "-=0.15")
      .from(".indicator-card", { autoAlpha: 0, y: 14, duration: 0.4, stagger: 0.06 }, "-=0.3")
      .from(".report-extra", { autoAlpha: 0, y: 10, duration: 0.4, stagger: 0.06 }, "-=0.2");
  }, { scope: root, dependencies: [company], revertOnUpdate: true });

  return (
    <main className="screen analysis-screen" ref={root}>
      <section className="zone zone-search compact" aria-label="Nova consulta">
        <SearchField consulta={consulta} compact />
      </section>

      <section className="zone zone-score" aria-labelledby="company-name">
        <div className="company-line">
          <div className="company-emblem" aria-hidden="true">{companyInitials(company.razaoSocial)}</div>
          <div>
            <span className="eyebrow">Dossiê {dossierNumber(company)}</span>
            <h1 id="company-name">{company.razaoSocial}</h1>
            <p className="company-sub">
              <span className="mono">{company.cnpj}</span>
              <span className="status-dot">{company.situacaoCadastral}</span>
              <span>{company.municipio}/{company.uf}</span>
            </p>
          </div>
        </div>
        <ScoreGauge score={company.scoreTotal} risk={company.classificacao} />
        <RiskBadge risk={company.classificacao} large />
        <p className="score-note">{riskNotes[company.classificacao]}</p>
      </section>

      <section className="zone zone-report" aria-labelledby="report-heading">
        <div className="report-inner">
          <div className="report-heading">
            <div>
              <span className="eyebrow">Relatório de indicadores</span>
              <h2 id="report-heading">{company.indicadores.length} sinais avaliados</h2>
            </div>
            <button className="secondary-button" onClick={onReport} type="button">
              <Icon><path d="M6 2h8l4 4v16H6zM14 2v5h5" /></Icon>
              Gerar relatório formal
            </button>
          </div>

          <ul className="indicator-list">
            {company.indicadores.map((indicador) => (
              <IndicatorCard indicador={indicador} key={indicador.id} />
            ))}
          </ul>

          <div className="report-extra panel company-details">
            <h3>Identificação cadastral</h3>
            <dl>
              {details.map(([term, value]) => (
                <div key={term}>
                  <dt>{term}</dt>
                  <dd>{value}</dd>
                </div>
              ))}
              <div className="wide">
                <dt>{company.socios.length > 1 ? "Sócios" : "Sócio"}</dt>
                {company.socios.slice(0, MAX_SOCIOS).map((socio) => (
                  <dd key={socio.nome}>
                    {socio.nome} ({socio.qualificacao})
                    {socio.cnpjsAtivos !== undefined && ` · ${socio.cnpjsAtivos} CNPJs ativos vinculados`}
                  </dd>
                ))}
                {company.socios.length > MAX_SOCIOS && <dd>e mais {company.socios.length - MAX_SOCIOS} sócios</dd>}
              </div>
            </dl>
          </div>

          <div className="report-extra source-bar">
            <Icon><path d="M12 22s8-3 8-10V5l-8-3-8 3v7c0 7 8 10 8 10Z" /><path d="m9 12 2 2 4-5" /></Icon>
            <span><strong>Fontes consultadas:</strong> Receita Federal (cadastro de CNPJ), Cadastro de instituições autorizadas do BACEN e Ranking de Reclamações do BACEN.</span>
            <span className="source-time">Consultado em {formatTimestamp(consultedAt)}</span>
          </div>
        </div>
      </section>
    </main>
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
  const root = useRef<HTMLElement>(null);

  useGSAP(() => {
    if (prefersReducedMotion()) return;
    gsap.from(".report-document", { autoAlpha: 0, y: 20, duration: 0.5, ease: "power2.out" });
    gsap.utils.toArray<HTMLElement>(".scroll-reveal").forEach((el) => {
      gsap.from(el, {
        autoAlpha: 0,
        y: 24,
        duration: 0.5,
        ease: "power2.out",
        scrollTrigger: { trigger: el, start: "top 88%", once: true },
      });
    });
  }, { scope: root });

  return (
    <main className="screen report-screen" ref={root}>
      <div className="report-toolbar">
        <div>
          <span className="eyebrow">Relatório de inteligência</span>
          <span className="toolbar-state"><i /> Documento concluído</span>
        </div>
        <div className="toolbar-actions">
          <button className="secondary-button" type="button">
            <Icon><path d="M12 3v12M7 10l5 5 5-5M5 21h14" /></Icon>
            Exportar PDF
          </button>
          <button className="primary-button" type="button">
            <Icon><path d="M18 8a3 3 0 1 0-2.8-4M6 14a3 3 0 1 0 0 6M18 14a3 3 0 1 0 0 6M8.7 17.5l6.6-3M8.7 6.5l6.6 3" /></Icon>
            Compartilhar
          </button>
        </div>
      </div>

      <article className="report-document">
        <header className="report-header">
          <div className="report-brand">
            <OwlLogo />
            <div><strong>EULE</strong><span>Ministério Público da Bahia · NUCCIBER</span></div>
          </div>
          <dl className="report-meta">
            <dt>Gerado em</dt><dd>{formatTimestamp(consultedAt)}</dd>
            <dt>Referência</dt><dd>{reference}</dd>
          </dl>
        </header>

        <div className="classification">Uso restrito · Informação de inteligência</div>
        <div className="report-title">
          <span>Relatório de análise pré-investigativa</span>
          <h1>{company.razaoSocial}</h1>
          <p>CNPJ {company.cnpj} · {company.municipio}/{company.uf} · Abertura em {company.dataAbertura}</p>
        </div>

        <section className={`executive-summary ${riskClass(company.classificacao)}`}>
          <div>
            <span className="report-label">01 · Sumário executivo</span>
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
          <div className="summary-score"><strong>{company.scoreTotal}</strong><span>Risco {company.classificacao}</span></div>
        </section>

        <section className="report-section scroll-reveal">
          <span className="report-label">02 · Matriz de indicadores</span>
          <div className="table-scroll">
            <table className="report-table">
              <thead><tr><th>Indicador analisado</th><th>Peso máx.</th><th>Score</th><th>Valor observado</th></tr></thead>
              <tbody>
                {company.indicadores.map((indicador) => (
                  <tr key={indicador.id}>
                    <td className="indicator-name-cell">{indicador.id} · {indicador.label}</td>
                    <td>{indicador.peso}</td>
                    <td className={`score-cell ${indicatorLevel(indicador)}`}>{indicador.score}</td>
                    <td><ObservadoCell texto={indicador.observado} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <div className="report-columns scroll-reveal">
          <section className="report-section legal-section">
            <span className="report-label">03 · Fundamentação jurídica</span>
            <h2>STF · Tema 1404 (RE 1.537.165)</h2>
            <p>A presente análise observa a tese de repercussão geral fixada pelo Supremo Tribunal Federal no Tema 1404 (RE 1.537.165), respeitando os limites de acesso, tratamento e compartilhamento de dados por órgãos de persecução.</p>
            <p>Considera-se ainda a Resolução Conjunta nº 16/2025 do BCB/CMN, que proíbe a utilização de contas-bolsão, com prazo até 31/12/2026.</p>
            <blockquote>O uso de informações deve preservar a finalidade pública, a rastreabilidade da consulta e a proporcionalidade da medida investigativa.</blockquote>
          </section>

          <section className="report-section recommendations">
            <span className="report-label">04 · Recomendações</span>
            <ol>
              {recommendations.map((item, index) => (
                <li key={item}><span>{String(index + 1).padStart(2, "0")}</span>{item}</li>
              ))}
            </ol>
          </section>
        </div>

        <footer className="report-footer">
          <p>Este documento é produto de inteligência e não constitui, isoladamente, prova de ilícito. As informações devem ser corroboradas por diligências próprias antes de qualquer medida administrativa ou judicial.</p>
          <span>EULE · {reference} · Página 1/1</span>
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
          <AnalysisScreen company={company} consultedAt={consultedAt} onLoad={loadCompany} onReport={() => setScreen("report")} />
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
