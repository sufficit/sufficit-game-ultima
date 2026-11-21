# Janelas do jogo declarativas: layout JSON + componentes Razor

## Objetivo e critérios

As janelas da interface do jogo (inventário, missões, diálogo, mapa) passam a
ser **declaradas em JSON de layout** (posição, tipo, controles, textura/skin) e
renderizadas por **componentes Razor gerados por tipo de janela**, em vez de
cada janela ser uma página/componente feito à mão. O dispatch janela→comportamento
fica centralizado.

Critérios de aceite:

- Formato de layout JSON documentado (janela, painéis, botões, listas, ligações
  de estado) com um exemplo válido no repo.
- Pelo menos duas janelas reais (diálogo da Guardiã + uma nova, ex.: diário de
  missões) descritas só por JSON, renderizadas pelos componentes Razor.
- Dispatch centralizado: mesma ação em janelas diferentes cai no mesmo
  manipulador; adicionar janela nova não copia/cola código de janela.
- Skins permutáveis (tema) sem mudar o layout.

## Terreno e referências

- Fonte: lineage-rust descreve as janelas C4 num layout declarativo
  (`interface.xdat`), pinta por ~90 módulos e usa dispatch gerado
  (`crates/killer-ui/src/xdat_view.rs`, `xdat_window.rs`,
  `window_dispatch_generated.rs`); o mini-HTML de NPC com links/botões
  (`crates/killer-ui/src/html.rs` + `html_layout.rs`) é referência de
  semântica para diálogos ricos.
- No Ultima a UI já é Razor (casca Blazor): o gain é o layout sair do código e
  virar conteúdo versionável junto com as DLLs/`app/`.

## Checkpoints

1. [pendente] Definir o esquema JSON de layout + componente Razor raiz que
   interpreta (janela, painel, botão, lista, vínculo de estado).
2. [pendente] Migrar o diálogo da Guardiã para layout JSON sem perder recurso
   atual (falas fixas, WebLLM, aviso da API).
3. [pendente] Segunda janela (diário/missões) só por JSON; dispatch central e
   tema permutável.
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1: esboçar o esquema JSON a partir do diálogo atual da Guardiã.
