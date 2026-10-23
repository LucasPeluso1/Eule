# Eule · Design

Linguagem: azulejaria modernista de Brasília (Athos Bulcão) sobre marinho profundo. Direção registrada em `.impeccable/surfaces/src-app-tsx.md`.

## Tokens (src/index.css)

- Chão `--ground #06102a`; placas `--ink-900 #0a1834` a 94% com borda `--line #213c6c` e sombra deslocada `--plate-shadow`.
- Cobalto `--cobalt #2f63d6` para ação primária e seleção; `--cobalt-text #82a8ff` para texto/realce sobre marinho; `--azulejo #d6e2f5` só no fundo.
- Estado (só com significado): vermelho `--red`, carmim `--crimson` (crítico), âmbar `--amber`, verde `--green`, cada um com variante `-text` e `-tint`.
- Raio único `--radius: 4px` (módulo de azulejo). Nada de pílulas.
- Tipografia: Archivo variável. Títulos e números grandes em `font-stretch: 112–125%`, peso 700–800; corpo 16px; dados com `tabular-nums` (sem fonte mono).

## Componentes

- Fundo `TileField` (canvas, GSAP): azulejos de 76px com seis motivos girando um a um; ao abrir uma análise, onda de giros a partir de `#score-anchor` tinge os azulejos próximos com a cor do risco. Estático com `prefers-reduced-motion`.
- Abas sublinhadas com marcador deslizante; score como número grande + régua de faixas 0/40/70/90 (padrão de `EuleOptions`).
- Indicadores em linhas de um único painel com medidor score/peso, não cartões.
- Relatório formal é uma folha clara (variáveis de cor redefinidas em `.report-document`), pronta para impressão.

## Proibido

Rótulos em caixa alta acima de títulos, barras coloridas à esquerda com mais de 1px, brilhos sem deslocamento, texto em gradiente, vidro decorativo.
