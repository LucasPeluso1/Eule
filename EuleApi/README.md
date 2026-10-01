# EuleApi

API do Eule (NUCCIBER/MPBA): consulta pré-investigativa de intermediadoras de pagamento usando apenas dados públicos.

## Rodar

```bash
cd C:/Eule/EuleApi
dotnet run --launch-profile EuleApi      # http://localhost:5080  (Swagger em /swagger)
dotnet test ../EuleApi.Tests
```

O projeto mira `net8.0`; `RollForward=LatestMajor` permite rodar em máquinas que só têm um runtime mais novo.

## Endpoints

- `GET /api/empresa/{cnpj}[?forcarAtualizacao=true]`: CNPJ com ou sem máscara (`%2F` ou `/` aceitos). 200, 400 (CNPJ inválido), 404 (inexistente na Receita), 503 (fonte externa indisponível, com o nome da fonte em `fonte`).
- `GET /api/health`

Cache em memória de 24h por CNPJ (e do índice do ranking). Erros nunca são cacheados.

## Fontes (todas testadas ao vivo)

| Fonte | Endpoint | Observações |
|---|---|---|
| Receita Federal | `https://publica.cnpj.ws/cnpj/{cnpj}` | Limite de ~3 req/min por IP (429 vira 503). |
| BACEN cadastro | Olinda `BcBase/v2` `EntidadesSupervisionadas`, filtro `codigoCNPJ8` | O endpoint `/api/listaentidades/lista` **não existe** (404) e as páginas do bcb.gov.br são SPA Angular; por isso foi usada a API Olinda oficial. |
| BACEN ranking | `www3.bcb.gov.br/rdrweb/rest/ext/ranking/` (lista JSON + `arquivo?...` CSV) | Pega o trimestre mais recente. O CSV só traz CNPJ em parte das linhas; conglomerados são casados por nome normalizado. |

## Indicadores (v1.0.0)

| Cód. | Peso | Regra |
|---|---|---|
| 01 | 25 | 25 × fator de idade × (0,6 + 0,4 × fator de capital). Limiares em `Score` no appsettings. |
| 02 | 25 | 25 se o CNPJ raiz não consta como autorizado no BACEN. |
| 03 | 20 | CNAE fora dos grupos 6492/6619 → 20. |
| 04 | 15 | **Manual** (retorna 0, `revisaoManual=true`, lista os sócios). |
| 05 | 10 | 10 se ausente do ranking (2 se empresa com menos de 3 meses). |
| 06 | 5 | **Manual** (retorna 0, `revisaoManual=true`). |

`scoreParcial=true` enquanto houver indicador manual pendente. Risco: ALTO >= 60, MEDIO >= 35, BAIXO abaixo disso.
