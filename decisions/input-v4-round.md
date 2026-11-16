# Decisão — rodada de melhorias v4 (input/UX)

Data: 2026-11-16. Guiada pelas skills próprias "Ultima Game Development" e
"Ultima Input and Game UX" (destiladas do repositório público
gamedev-skills/awesome-gamedev-agent-skills).

## Escopo desta rodada
1. Gamepad: deadzone **radial** (antes: eixo a eixo, distorcia diagonais);
   D-pad (botões 12-15) como alternativa ao stick esquerdo; botão B/Círculo
   (índice 1) e Start (9) fecham diálogo/cancelam (edge, não repetição);
   detecção de dispositivo ativo.
2. Dica de interação ("pressione E / A no controle / toque em A") passa a
   acompanhar o último dispositivo usado via `body[data-input]` (teclado,
   gamepad ou toque) — princípio "último dispositivo" da skill input-systems.
3. Reset de estado de botões em `gamepaddisconnected` e continuação do teclado.
4. Mantidos intactos: WASD/setas, Shift, câmera por mouse, sticks virtuais de
   toque, fluxo de diálogo E/Enter/Esc, P2P, voz e aviso de atualização
   (t19/t20 já implementados em commit anterior 1f8dd20).

## Itens avaliados e adiados
- t14 (captura de microfone/voice chat P2P): voice.js já existe; validação de
  dois peers fica para rodada própria.
- t15 (robustez P2P): net.js já contém reenvio de offer, STUN duplo,
  restartIce e syncPeers (compilados em rodada anterior); falta validação com
  segundo peer real — segue pendente.

## Verificação até agora
- `node --check` OK em game.js.
- Teste unitário da deadzone radial em Node: (0,5,0)→(0,39,0); (1,0)→(1,0);
  diagonal (0,7071;0,7071)→magnitude 1,0 preservada; (0,3;0,3)→0,298;
  vetores fora de alcance limitados a 1.
- Validação em navegador e checkpoint: pendentes desta rodada (ver abaixo).
