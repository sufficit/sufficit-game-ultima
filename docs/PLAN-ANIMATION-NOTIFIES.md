# Notifies: eventos de gameplay amarrados à timeline das animações

## Objetivo e critérios

Animações de personagens com **notifies**: marcas com tempo normalizado na
timeline que disparam gameplay (hit, som, efeito, mudança de estado) no
instante exato, em vez de timers paralelos estimando a duração da animação.

Critérios de aceite:

- Formato de notify no dado da animação (nome + tempo normalizado + payload)
   versionado junto com os modelos das DLLs.
- Runtime: ao cruzar o tempo do notify no frame (inclusive com wrap de loop),
  o evento vai para a fila do NPC (ver
  [PLAN-NPC-AI-DECISIONS.md](PLAN-NPC-AI-DECISIONS.md)) — dano/hit só ocorre
  pelo notify.
- Uma animação real do jogo com ao menos dois notifies comprovados (ex.: golpe
  com impacto no meio e som no fim).
- Compatível com pausa/mudança de animação no meio: notifies não disparados
  são descartados sem efeito.

## Terreno e referências

- Fonte: `crates/killer-assets/src/animation.rs` do lineage-rust
  (`sequence_timing` com notifies; amostragem nlerp com wrap de loop) e
  `crates/killer-world/src/anim/` (máquina de canais, `notify.rs`,
  `AnimNotify_AttackShot` disparando hits/efeitos/sons).
- Equivalentes Babylon: `AnimationGroup` + observables por frame/intervalo —
  o dado (tempo normalizado no asset) e o cruzamento com wrap são a parte a
  implementar.

## Checkpoints

1. [pendente] Estender o formato de animação dos modelos com notifies + runtime
   de cruzamento (com wrap de loop) testado unitariamente.
2. [pendente] Primeira animação real com notifies exercitando gameplay (hit e
   som) no site publicado.
3. [pendente] Documentar o formato no pipeline de build das DLLs
   (`scripts/build-models.sh`).
4. [pendente] Converter este plano em atividade.

## Próxima ação

Checkpoint 1 após existir animação esquelética no pipeline (ver
[PLAN-CROWD-SKINNING.md](PLAN-CROWD-SKINNING.md)).

Depende de: pipeline de modelos com animação; fila de eventos do plano de IA.
