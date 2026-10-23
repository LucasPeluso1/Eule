# Product

<!-- impeccable:product-schema 1 -->

> Registro inferido do repositório e do pedido do usuário (sem rodada de entrevista); campos marcados como _inferido_ devem ser confirmados.

## Platform

web

## Users

Analistas e membros do NUCCIBER (Núcleo de Combate aos Crimes Cibernéticos) do Ministério Público da Bahia. Usam a ferramenta em estação de trabalho, durante horas, para triagem de empresas suspeitas antes de abrir procedimento. _inferido_

## Product Purpose

Eule é um sistema de inteligência pré-investigativa: recebe um CNPJ, cruza dados públicos (Receita Federal, BACEN, CEIS/CNEP, PGFN, DataJud) e devolve um score de risco 0–100 com indicadores do tipo INDICIO_JURIDICO e FATOR_RISCO. O objetivo é dizer, com fundamentação, se há justa causa para instaurar PIC ou requisitar IP (STF Tema 1404).

## Positioning

A combinação de indícios jurídicos com fatores de risco gera um alerta de justa causa (FUNDAMENTO_PIC, SUSPEITA_QUALIFICADA) e um relatório formal de inteligência pronto para o processo.

## Operating Context

Fluxo em três telas: consulta por CNPJ com histórico da sessão; análise com score, banner de justa causa e cartões de indicadores; relatório formal com sumário executivo, matriz de indicadores, fundamentação jurídica e recomendações, marcado como "Uso restrito · Informação de inteligência".

## Capabilities and Constraints

- Frontend React 19 + TypeScript + Vite + Tailwind v4, animações com GSAP.
- API ASP.NET Core em `http://localhost:5080`; o frontend não deve alterar contratos, tipos ou chamadas.
- Exportar PDF e Compartilhar ainda não têm implementação.

## Brand Commitments

- Nome Eule (coruja, em alemão) e o logotipo da coruja.
- Sensação séria de inteligência governamental, base azul-marinho (pedido do usuário).
- Fundo animado é requisito explícito do usuário (out/2026).

## Evidence on Hand

Nenhum dado real além do retornado pela API; não inventar casos, números de desempenho ou órgãos parceiros.

## Product Principles

- O veredito (score, classificação, justa causa) precisa ser legível em segundos.
- Toda afirmação de risco vem com o valor observado que a fundamenta.
- O relatório deve parecer um documento oficial, não um painel.

## Accessibility & Inclusion

Uso prolongado: contraste AA, corpo de 16px, respeito a `prefers-reduced-motion`. _inferido_
