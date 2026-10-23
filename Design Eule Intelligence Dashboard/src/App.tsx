import { FormEvent, ReactNode, useEffect, useRef, useState } from "react";
import gsap from "gsap";
import { useGSAP } from "@gsap/react";
import { fetchEmpresa } from "./api";
import { CompanyData, Indicador, RecentSearch, Risk, TipoAlerta, TipoIndicador } from "./fixtures";
import TileField from "./TileField";

gsap.registerPlugin(useGSAP);

type Screen = "search" | "analysis" | "report";

const riskNotes: Record<Risk, string> = {
  CRÍTICO: "Risco crítico. Os sinais reunidos justificam prioridade na análise e avaliação imediata de medidas.",
  ALTO: "Risco elevado. Recomenda-se análise aprofundada antes do prosseguimento.",
  MÉDIO: "Risco moderado. Recomenda-se acompanhamento e verificação complementar.",
  BAIXO: "Risco baixo. Nenhuma providência adicional recomendada neste momento.",
};

/** Faixas padrão de classificação da API (EuleOptions): BAIXO < 40 ≤ MÉDIO < 70 ≤ ALTO < 90 ≤ CRÍTICO. */
const riskBands: { risk: Risk; from: number; to: number }[] = [
  { risk: "BAIXO", from: 0, to: 40 },
  { risk: "MÉDIO", from: 40, to: 70 },
  { risk: "ALTO", from: 70, to: 90 },
  { risk: "CRÍTICO", from: 90, to: 100 },
];

/** Bases cruzadas a cada consulta, na ordem em que aparecem no relatório. */
const sources = [
  ["Receita Federal", "Cadastro CNPJ e quadro societário"],
  ["Banco Central", "Instituições autorizadas"],
  ["Banco Central", "Ranking de reclamações"],
  ["CGU", "Sanções CEIS e CNEP"],
  ["PGFN", "Dívida ativa da União"],
  ["CNJ", "Processos no DataJud"],
];

const prefersReducedMotion = () =>
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function indicatorLevel(indicador: Indicador) {
  // A API marca com "NÃO AVALIADO" os indicadores cuja fonte complementar não respondeu.
  if (indicador.score === 0 && indicador.observado.startsWith("NÃO AVALIADO")) return "na";
  const ratio = indicador.score / indicador.peso;
  if (ratio >= 0.66) return "high";
  if (ratio >= 0.33) return "medium";
  return "low";
}

function riskClass(risk: Risk) {
  return risk.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

const tipoLabels: Record<TipoIndicador, string> = {
  INDICIO_JURIDICO: "Indício jurídico",
  FATOR_RISCO: "Fator de risco",
};

function TipoTag({ tipo }: { tipo: TipoIndicador }) {
  return <span className={tipo === "INDICIO_JURIDICO" ? "tag juridico" : "tag risco"}>{tipoLabels[tipo]}</span>;
}

/** Alertas que exigem indício jurídico positivo e por isso ganham o banner de justa causa. */
const alertCopy: Partial<Record<TipoAlerta, { kicker: string; title: string; text: string }>> = {
  FUNDAMENTO_PIC: {
    kicker: "Justa causa identificada",
    title: "Fundamento para instauração de PIC",
    text: "Score de risco a partir de 90 com indício jurídico positivo. Os elementos reunidos indicam justa causa para instauração de PIC ou requisição de IP, nos termos do Tema 1404 do STF.",
  },
  SUSPEITA_QUALIFICADA: {
    kicker: "Suspeita qualificada",
    title: "Indício jurídico com score elevado",
    text: "Score de risco a partir de 80 com indício jurídico positivo. Recomenda-se diligência complementar para consolidar a justa causa antes da instauração de PIC.",
  },
};

function JustaCausaBanner({ company }: { company: CompanyData }) {
  const copy = alertCopy[company.tipoAlertaGeral];
  if (!copy) return null;
  const indicios = company.indicadores.filter((i) => i.tipo === "INDICIO_JURIDICO" && i.score > 0);
  const qualificada = company.tipoAlertaGeral === "SUSPEITA_QUALIFICADA";

  return (
    <section className={qualificada ? "alert-banner qualificada" : "alert-banner"} role="alert" aria-live="assertive">
      <div className="alert-icon" aria-hidden="true">
        <Icon>
          <path d="M12 3 2.5 20h19L12 3Z" />
          <path d="M12 10v4.5M12 17.5v.01" />
        </Icon>
      </div>
      <div className="alert-copy">
        <h2>{copy.title}</h2>
        <p className="alert-kicker"><i aria-hidden="true" />{copy.kicker}</p>
        <p>{copy.text}</p>
        {indicios.length > 0 && (
          <ul className="alert-indicios">
            {indicios.map((i) => (
              <li key={i.id}><TipoTag tipo={i.tipo} /> {i.id} · {i.label}</li>
            ))}
          </ul>
        )}
      </div>
      <div className="alert-score">
        <strong>{company.scoreTotal}</strong>
        <span>de 100</span>
      </div>
    </section>
  );
}

function situacaoFonte(valor: boolean | null, positivo: string, negativo: string) {
  if (valor === null) return "Não avaliado (fonte indisponível)";
  return valor ? positivo : negativo;
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
  const nav = useRef<HTMLElement>(null);
  const marker = useRef<HTMLSpanElement>(null);
  const placed = useRef(false);

  useGSAP(() => {
    const ativo = nav.current?.querySelector<HTMLElement>(".tab.active");
    if (!ativo || !marker.current) return;
    const destino = { x: ativo.offsetLeft, width: ativo.offsetWidth };
    if (!placed.current || prefersReducedMotion()) gsap.set(marker.current, destino);
    else gsap.to(marker.current, { ...destino, duration: 0.42, ease: "expo.out" });
    placed.current = true;
  }, { dependencies: [screen], scope: nav });

  useEffect(() => {
    const reposicionar = () => {
      const ativo = nav.current?.querySelector<HTMLElement>(".tab.active");
      if (ativo && marker.current) gsap.set(marker.current, { x: ativo.offsetLeft, width: ativo.offsetWidth });
    };
    window.addEventListener("resize", reposicionar);
    document.fonts?.ready.then(reposicionar);
    return () => window.removeEventListener("resize", reposicionar);
  }, []);

  return (
    <header className="app-header">
      <button className="brand" onClick={() => setScreen("search")} type="button">
        <OwlLogo />
        <span className="brand-copy">
          <strong>Eule</strong>
          <small>NUCCIBER · Ministério Público da Bahia</small>
        </span>
      </button>
      <nav className="tabs" aria-label="Navegação principal" ref={nav}>
        <span className="tab-marker" ref={marker} aria-hidden="true" />
        <button
          className={screen === "search" ? "tab active" : "tab"}
          aria-current={screen === "search" ? "page" : undefined}
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
          aria-current={screen === "analysis" ? "page" : undefined}
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
          aria-current={screen === "report" ? "page" : undefined}
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
        <div className="avatar" title="Usuário MP">MP</div>
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
        <h1>Nenhuma empresa consultada</h1>
        <p>A análise e o relatório aparecem aqui depois que você consultar um CNPJ.</p>
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

  const alertas = recentSearches.filter((item) => item.company.classificacao === "ALTO" || item.company.classificacao === "CRÍTICO").length;

  return (
    <main className="screen search-screen">
      <section className="panel intake" aria-labelledby="intake-title">
        <div className="intake-main">
          <h1 id="intake-title">Consulta de pessoa jurídica</h1>
          <p className="lede">
            Cruza dados públicos da Receita Federal e do Banco Central para identificar intermediadoras de pagamento
            suspeitas e apontar justa causa para PIC ou IP.
          </p>
          <SearchField consulta={consulta} />
        </div>
        <div className="intake-sources">
          <h2>Bases cruzadas em cada consulta</h2>
          <ol className="source-list">
            {sources.map(([orgao, base]) => (
              <li key={base}>
                <span className="source-glyph" aria-hidden="true" />
                <span><strong>{orgao}</strong>{base}</span>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <dl className="session-line" aria-label="Estatísticas da sessão">
        <div>
          <dt>Consultas nesta sessão</dt>
          <dd>{recentSearches.length}</dd>
        </div>
        <div className={alertas > 0 ? "alert-stat" : undefined}>
          <dt>Alertas de risco alto ou crítico</dt>
          <dd>{alertas}</dd>
        </div>
        <div>
          <dt>Última consulta</dt>
          <dd>{recentSearches[0]?.date ?? "Nenhuma ainda"}</dd>
        </div>
      </dl>

      <section className="panel recent-panel">
        <div className="section-heading">
          <h2>Consultas recentes</h2>
          <span>{recentSearches.length === 0 ? "Sem registros" : `${recentSearches.length} de até 5`}</span>
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
                  <td colSpan={5} className="empty-row">As consultas desta sessão ficam listadas aqui para você reabrir a análise.</td>
                </tr>
              )}
              {recentSearches.map(({ company, date }) => (
                <tr key={company.cnpj}>
                  <td className="num">{company.cnpj}</td>
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

function ScoreScale({ score, risk }: { score: number; risk: Risk }) {
  const root = useRef<HTMLDivElement>(null);
  const marker = useRef<HTMLSpanElement>(null);
  const value = useRef<HTMLElement>(null);

  useGSAP(() => {
    const estado = { v: 0 };
    const desenhar = () => {
      const v = Math.min(Math.max(estado.v, 0), 100);
      marker.current?.style.setProperty("left", `${v}%`);
      if (value.current) value.current.textContent = String(Math.round(estado.v));
    };

    if (prefersReducedMotion()) {
      estado.v = score;
      desenhar();
      return;
    }
    desenhar();
    gsap.to(estado, { v: score, duration: 1.1, ease: "expo.out", delay: 0.15, onUpdate: desenhar });
  }, { scope: root, dependencies: [score] });

  return (
    <div className={`score-block ${riskClass(risk)}`} ref={root} id="score-anchor">
      <div className="score-figure">
        <strong ref={value} aria-hidden="true">0</strong>
        <span>de 100</span>
      </div>
      <div className="scale" role="img" aria-label={`Score de risco ${score} de 100, faixa ${risk.toLowerCase()}`}>
        <div className="scale-track">
          {riskBands.map((band) => (
            <span
              key={band.risk}
              className={`scale-band ${riskClass(band.risk)}${band.risk === risk ? " current" : ""}`}
              style={{ flexGrow: band.to - band.from }}
            />
          ))}
          <span className="scale-marker" ref={marker} />
        </div>
        <div className="scale-labels" aria-hidden="true">
          {riskBands.map((band) => (
            <span key={band.risk} style={{ flexGrow: band.to - band.from }} className={band.risk === risk ? "current" : undefined}>
              <b>{band.from}</b>{band.risk.toLowerCase()}
            </span>
          ))}
        </div>
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
  na: "Não avaliado",
};

function IndicatorRow({ indicador }: { indicador: Indicador }) {
  const level = indicatorLevel(indicador);
  const proporcao = indicador.peso > 0 ? indicador.score / indicador.peso : 0;

  return (
    <li className={`indicator-row ${level}`}>
      <span className="indicator-code">{indicador.id}</span>
      <div className="indicator-main">
        <h3>{indicador.label}</h3>
        <p className="indicator-description"><ObservadoCell texto={indicador.observado} /></p>
        <TipoTag tipo={indicador.tipo} />
      </div>
      <div className="indicator-measure">
        <span className="indicator-score">
          <strong>{indicador.score}</strong>
          <small>de {indicador.peso}</small>
        </span>
        <span className="meter" aria-hidden="true"><i style={{ width: `${proporcao * 100}%` }} /></span>
        <span className={`level-label ${level}`}>{levelLabels[level]}</span>
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
    ["Sanções CEIS/CNEP (CGU)", situacaoFonte(
      company.fontes.sancaoCeis === null && company.fontes.sancaoCnep === null ? null : company.fontes.sancaoCeis === true || company.fontes.sancaoCnep === true,
      company.fontes.descricaoSancao ?? "Sanção ativa",
      "Sem sanção registrada",
    )],
    ["Dívida ativa da União (PGFN)", situacaoFonte(
      company.fontes.dividaAtivaPgfn,
      `Inscrita · ${company.fontes.valorDividaPgfn ?? "valor não informado"}`,
      "Sem dívida ativa",
    )],
    ["Processo criminal de sócio (DataJud)", company.fontes.socioProcessoCriminal === null
      ? "Não avaliado (CPF mascarado ou fonte indisponível)"
      : company.fontes.socioProcessoCriminal
        ? `${company.fontes.nomeSocioProcessado} · ${company.fontes.numeroProcessos} processo(s)`
        : "Nenhum processo criminal encontrado"],
  ];

  useGSAP(() => {
    if (prefersReducedMotion()) return;
    const tl = gsap.timeline({ defaults: { ease: "expo.out" } });
    tl.from(".verdict", { autoAlpha: 0, y: 10, duration: 0.5 })
      .from(".alert-banner", { autoAlpha: 0, clipPath: "inset(0 100% 0 0)", duration: 0.8, ease: "power4.inOut" }, 0.35)
      .from(".meter i", { scaleX: 0, transformOrigin: "left center", duration: 0.9, stagger: 0.05 }, 0.45);
  }, { scope: root, dependencies: [company], revertOnUpdate: true });

  return (
    <main className="screen analysis-screen" ref={root}>
      <section className="analysis-bar" aria-label="Nova consulta">
        <p className="analysis-crumb">Dossiê <span className="num">{dossierNumber(company)}</span> · consultado em {formatTimestamp(consultedAt)}</p>
        <SearchField consulta={consulta} compact />
      </section>

      <section className="panel verdict" aria-labelledby="company-name">
        <div className="company-line">
          <div className="company-emblem" aria-hidden="true">{companyInitials(company.razaoSocial)}</div>
          <div>
            <h1 id="company-name">{company.razaoSocial}</h1>
            <p className="company-sub">
              <span className="num">{company.cnpj}</span>
              <span className="status-dot">{company.situacaoCadastral}</span>
              <span>{company.municipio}/{company.uf}</span>
            </p>
            <div className="verdict-note">
              <RiskBadge risk={company.classificacao} large />
              <p className="score-note">{riskNotes[company.classificacao]}</p>
            </div>
          </div>
        </div>
        <ScoreScale score={company.scoreTotal} risk={company.classificacao} />
      </section>

      <section className="zone-report" aria-labelledby="report-heading">
        <div className="report-inner">
          <JustaCausaBanner company={company} />

          <div className="report-heading">
            <h2 id="report-heading">{company.indicadores.length} sinais avaliados</h2>
            <button className="secondary-button" onClick={onReport} type="button">
              <Icon><path d="M6 2h8l4 4v16H6zM14 2v5h5" /></Icon>
              Gerar relatório formal
            </button>
          </div>

          <ul className="panel indicator-list">
            {company.indicadores.map((indicador) => (
              <IndicatorRow indicador={indicador} key={indicador.id} />
            ))}
          </ul>

          <div className="panel company-details">
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

          <div className="source-bar">
            <Icon><path d="M12 22s8-3 8-10V5l-8-3-8 3v7c0 7 8 10 8 10Z" /><path d="m9 12 2 2 4-5" /></Icon>
            <span><strong>Fontes consultadas:</strong> Receita Federal (cadastro de CNPJ), Cadastro de instituições autorizadas e Ranking de Reclamações do BACEN, CEIS/CNEP (CGU), lista de devedores da PGFN e DataJud (CNJ).</span>
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
    gsap.from(".report-document", { autoAlpha: 0, y: 28, duration: 0.7, ease: "expo.out" });
  }, { scope: root });

  return (
    <main className="screen report-screen" ref={root}>
      <div className="report-toolbar">
        <div>
          <h1 className="toolbar-title">Relatório de inteligência</h1>
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
            <div><strong>Eule</strong><span>Ministério Público da Bahia · NUCCIBER</span></div>
          </div>
          <dl className="report-meta">
            <dt>Gerado em</dt><dd>{formatTimestamp(consultedAt)}</dd>
            <dt>Referência</dt><dd>{reference}</dd>
          </dl>
        </header>

        <div className="classification">Uso restrito · Informação de inteligência</div>
        <div className="report-title">
          <h2>{company.razaoSocial}</h2>
          <p className="report-kind">Relatório de análise pré-investigativa</p>
          <p className="report-id">CNPJ <span className="num">{company.cnpj}</span> · {company.municipio}/{company.uf} · Abertura em {company.dataAbertura}</p>
        </div>

        <div className="report-alert">
          <JustaCausaBanner company={company} />
        </div>

        <section className={`executive-summary ${riskClass(company.classificacao)}`}>
          <div>
            <h3><span>1</span>Sumário executivo</h3>
            <p className="summary-lead">Classificação de risco {company.classificacao.toLowerCase()}.</p>
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

        <section className="report-section">
          <h3><span>2</span>Matriz de indicadores</h3>
          <div className="table-scroll">
            <table className="report-table">
              <thead><tr><th>Indicador analisado</th><th>Tipo</th><th>Peso máx.</th><th>Score</th><th>Valor observado</th></tr></thead>
              <tbody>
                {company.indicadores.map((indicador) => (
                  <tr key={indicador.id}>
                    <td className="indicator-name-cell">{indicador.id} · {indicador.label}</td>
                    <td><TipoTag tipo={indicador.tipo} /></td>
                    <td>{indicador.peso}</td>
                    <td className={`score-cell ${indicatorLevel(indicador)}`}>{indicador.score}</td>
                    <td><ObservadoCell texto={indicador.observado} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <div className="report-columns">
          <section className="report-section legal-section">
            <h3><span>3</span>Fundamentação jurídica</h3>
            <p className="legal-ref">STF · Tema 1404 (RE 1.537.165)</p>
            <p>A presente análise observa a tese de repercussão geral fixada pelo Supremo Tribunal Federal no Tema 1404 (RE 1.537.165), respeitando os limites de acesso, tratamento e compartilhamento de dados por órgãos de persecução.</p>
            <p>Considera-se ainda a Resolução Conjunta nº 16/2025 do BCB/CMN, que proíbe a utilização de contas-bolsão, com prazo até 31/12/2026.</p>
            <blockquote>O uso de informações deve preservar a finalidade pública, a rastreabilidade da consulta e a proporcionalidade da medida investigativa.</blockquote>
          </section>

          <section className="report-section recommendations">
            <h3><span>4</span>Recomendações</h3>
            <ol>
              {recommendations.map((item, index) => (
                <li key={item}><span>{index + 1}</span>{item}</li>
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
      <TileField pulse={screen === "analysis" && company ? { key: consultedAt.getTime(), risk: company.classificacao } : null} />
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
