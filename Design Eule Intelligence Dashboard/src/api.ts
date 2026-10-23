import type { CompanyData, Risk, TipoAlerta, TipoIndicador } from "./fixtures";

const API_URL = "http://localhost:5080";

interface EmpresaApi {
  cnpjFormatado: string;
  razaoSocial: string;
  nomeFantasia: string | null;
  dataAbertura: string;
  capitalSocial: number;
  cnae: string;
  cnaeDescricao: string;
  porte: string | null;
  uf: string | null;
  municipio: string | null;
  situacaoCadastral: string;
  socios: Array<{ nome: string; qualificacao: string }>;
  autorizadoBacen: boolean;
  constaNoRanking: boolean;
  sancaoAtivaCeis: boolean | null;
  sancaoAtivaCnep: boolean | null;
  descricaoSancao: string | null;
  possuiDividaAtivaPgfn: boolean | null;
  valorDividaPgfn: number | null;
  socioComProcessoCriminal: boolean | null;
  nomeSocioProcessado: string | null;
  numeroProcessosEncontrados: number | null;
  indicadores: Array<{
    codigo: string;
    descricao: string;
    pesoMaximo: number;
    scoreObtido: number;
    fundamentacao: string;
    tipo: TipoIndicador;
  }>;
  scoreTotal: number;
  nivelRisco: "CRITICO" | "ALTO" | "MEDIO" | "BAIXO";
  tipoAlertaGeral: TipoAlerta;
}

const riscos: Record<EmpresaApi["nivelRisco"], Risk> = {
  CRITICO: "CRÍTICO",
  ALTO: "ALTO",
  MEDIO: "MÉDIO",
  BAIXO: "BAIXO",
};

const formatarData = (iso: string) => iso.split("-").reverse().join("/");

const formatarMoeda = (valor: number) =>
  valor.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

function mapear(e: EmpresaApi): CompanyData {
  return {
    cnpj: e.cnpjFormatado,
    razaoSocial: e.razaoSocial,
    nomeFantasia: e.nomeFantasia ?? "—",
    dataAbertura: formatarData(e.dataAbertura),
    situacaoCadastral: e.situacaoCadastral,
    capitalSocial: formatarMoeda(e.capitalSocial),
    cnae: e.cnae,
    cnaeDescricao: e.cnaeDescricao,
    porte: e.porte ?? "—",
    uf: e.uf ?? "—",
    municipio: e.municipio ?? "—",
    socios: e.socios.map((s) => ({ nome: s.nome, qualificacao: s.qualificacao })),
    apareceBacen: e.autorizadoBacen,
    aparecRanking: e.constaNoRanking,
    indicadores: e.indicadores.map((i) => ({
      id: i.codigo,
      label: i.descricao,
      peso: i.pesoMaximo,
      score: i.scoreObtido,
      observado: i.fundamentacao,
      tipo: i.tipo ?? "FATOR_RISCO",
    })),
    fontes: {
      sancaoCeis: e.sancaoAtivaCeis ?? null,
      sancaoCnep: e.sancaoAtivaCnep ?? null,
      descricaoSancao: e.descricaoSancao ?? null,
      dividaAtivaPgfn: e.possuiDividaAtivaPgfn ?? null,
      valorDividaPgfn: e.valorDividaPgfn == null ? null : formatarMoeda(e.valorDividaPgfn),
      socioProcessoCriminal: e.socioComProcessoCriminal ?? null,
      nomeSocioProcessado: e.nomeSocioProcessado ?? null,
      numeroProcessos: e.numeroProcessosEncontrados ?? null,
    },
    scoreTotal: e.scoreTotal,
    classificacao: riscos[e.nivelRisco],
    tipoAlertaGeral: e.tipoAlertaGeral ?? "RISCO_MODERADO",
  };
}

export async function fetchEmpresa(cnpj: string): Promise<CompanyData> {
  const digitos = cnpj.replace(/\D/g, "");

  let resposta: Response;
  try {
    resposta = await fetch(`${API_URL}/api/empresa/${digitos}`);
  } catch {
    throw new Error("Sem conexão com o servidor");
  }

  if (resposta.status === 404) throw new Error("CNPJ não encontrado");
  if (resposta.status === 400) throw new Error("CNPJ inválido");
  if (resposta.status === 503) {
    const problema = await resposta.json().catch(() => null);
    throw new Error("Serviço indisponível: " + (problema?.detail ?? "tente novamente em instantes."));
  }
  if (!resposta.ok) throw new Error(`Erro inesperado do servidor (HTTP ${resposta.status})`);

  return mapear((await resposta.json()) as EmpresaApi);
}
