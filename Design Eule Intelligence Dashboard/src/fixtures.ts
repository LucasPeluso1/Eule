export type Risk = "CRÍTICO" | "ALTO" | "MÉDIO" | "BAIXO";

export type TipoIndicador = "INDICIO_JURIDICO" | "FATOR_RISCO";

export type TipoAlerta = "FUNDAMENTO_PIC" | "SUSPEITA_QUALIFICADA" | "RISCO_ELEVADO" | "RISCO_MODERADO";

export interface Socio {
  nome: string;
  qualificacao: string;
  /** Quantidade de CNPJs ativos vinculados ao sócio, quando conhecida. */
  cnpjsAtivos?: number;
}

export interface Indicador {
  id: string;
  label: string;
  /** Peso máximo do indicador na pontuação. */
  peso: number;
  score: number;
  observado: string;
  tipo: TipoIndicador;
}

/** Fontes complementares (indicadores 07–09). `null` significa fonte indisponível ou não consultável. */
export interface FontesComplementares {
  sancaoCeis: boolean | null;
  sancaoCnep: boolean | null;
  descricaoSancao: string | null;
  dividaAtivaPgfn: boolean | null;
  valorDividaPgfn: string | null;
  socioProcessoCriminal: boolean | null;
  nomeSocioProcessado: string | null;
  numeroProcessos: number | null;
}

export interface CompanyData {
  cnpj: string;
  razaoSocial: string;
  nomeFantasia: string;
  dataAbertura: string;
  situacaoCadastral: string;
  capitalSocial: string;
  cnae: string;
  cnaeDescricao: string;
  porte: string;
  uf: string;
  municipio: string;
  socios: Socio[];
  apareceBacen: boolean;
  aparecRanking: boolean;
  indicadores: Indicador[];
  fontes: FontesComplementares;
  scoreTotal: number;
  classificacao: Risk;
  tipoAlertaGeral: TipoAlerta;
}

const semFontesComplementares: FontesComplementares = {
  sancaoCeis: null,
  sancaoCnep: null,
  descricaoSancao: null,
  dividaAtivaPgfn: null,
  valorDividaPgfn: null,
  socioProcessoCriminal: null,
  nomeSocioProcessado: null,
  numeroProcessos: null,
};

const sumScores = (indicadores: Indicador[]) =>
  indicadores.reduce((total, item) => total + item.score, 0);

export const globalVenture: CompanyData = {
  cnpj: "68.080.907/0001-41",
  razaoSocial: "Global Venture Tecnologia LTDA",
  nomeFantasia: "—",
  dataAbertura: "17/07/2026",
  situacaoCadastral: "Ativa",
  capitalSocial: "R$ 50.000,00",
  cnae: "62.01-5-01",
  cnaeDescricao: "Desenvolvimento de programas de software sob encomenda",
  porte: "Microempresa",
  uf: "PR",
  municipio: "Medianeira",
  socios: [{ nome: "Marcos Vinicius Nascimento da Silva", qualificacao: "Sócio-administrador" }],
  apareceBacen: false,
  aparecRanking: false,
  indicadores: [
    { id: "01", label: "Constituição recente com capital incompatível", peso: 25, score: 25, observado: "Aberta em 17/07/2026 (menos de 3 meses), capital social de R$ 50.000,00", tipo: "FATOR_RISCO" },
    { id: "02", label: "Ausência de autorização regulatória BACEN", peso: 25, score: 25, observado: "Não consta no Cadastro de instituições autorizadas pelo BACEN", tipo: "FATOR_RISCO" },
    { id: "03", label: "Inconsistência de objeto social (CNAE vs atividade real)", peso: 20, score: 20, observado: "CNAE 62.01-5-01 (desenvolvimento de software) incompatível com intermediação de pagamentos", tipo: "FATOR_RISCO" },
    { id: "04", label: "Cruzamento societário atípico", peso: 15, score: 0, observado: "Sem vínculos societários atípicos identificados", tipo: "FATOR_RISCO" },
    { id: "05", label: "Ausência no Ranking de Reclamações BACEN", peso: 10, score: 2, observado: "Não consta no ranking; empresa com menos de 3 meses, sem histórico suficiente", tipo: "FATOR_RISCO" },
    { id: "06", label: "Inconsistência geográfica (sede vs operação)", peso: 5, score: 0, observado: "Sem inconsistência geográfica identificada", tipo: "FATOR_RISCO" },
  ],
  fontes: semFontesComplementares,
  scoreTotal: 72,
  classificacao: "ALTO",
  tipoAlertaGeral: "RISCO_ELEVADO",
};

const caitecIndicadores: Indicador[] = [
  { id: "01", label: "Constituição recente com capital incompatível", peso: 25, score: 18, observado: "Aberta em 28/04/2026, capital social de R$ 100.000,00", tipo: "FATOR_RISCO" },
  { id: "02", label: "Ausência de autorização regulatória BACEN", peso: 25, score: 25, observado: "Não consta no Cadastro de instituições autorizadas pelo BACEN", tipo: "FATOR_RISCO" },
  { id: "03", label: "Inconsistência de objeto social (CNAE vs atividade real)", peso: 20, score: 20, observado: "CNAE 73.19-0-03 (marketing direto) incompatível com intermediação de pagamentos", tipo: "FATOR_RISCO" },
  { id: "04", label: "Cruzamento societário atípico", peso: 15, score: 15, observado: "Sócia-administradora com 19 CNPJs ativos vinculados", tipo: "FATOR_RISCO" },
  { id: "05", label: "Ausência no Ranking de Reclamações BACEN", peso: 10, score: 0, observado: "Não registrada no ranking", tipo: "FATOR_RISCO" },
  { id: "06", label: "Inconsistência geográfica (sede vs operação)", peso: 5, score: 0, observado: "Sem inconsistência geográfica identificada", tipo: "FATOR_RISCO" },
];

export const caitec: CompanyData = {
  cnpj: "66.503.941/0001-56",
  razaoSocial: "Caitec LTDA",
  nomeFantasia: "—",
  dataAbertura: "28/04/2026",
  situacaoCadastral: "Ativa",
  capitalSocial: "R$ 100.000,00",
  cnae: "73.19-0-03",
  cnaeDescricao: "Marketing direto",
  porte: "Microempresa",
  uf: "SP",
  municipio: "São Paulo",
  socios: [{ nome: "Lorrane Vitoria Soares Cunha", qualificacao: "Sócia-administradora", cnpjsAtivos: 19 }],
  apareceBacen: false,
  aparecRanking: false,
  indicadores: caitecIndicadores,
  fontes: semFontesComplementares,
  scoreTotal: sumScores(caitecIndicadores),
  classificacao: "ALTO",
  tipoAlertaGeral: "RISCO_ELEVADO",
};

export interface RecentSearch {
  company: CompanyData;
  date: string;
}

export const recentSearches: RecentSearch[] = [];
