# Corrigir o poço e detalhar o terreno do Ultima

Critérios de aceite: a escavação existente em (10, 10) exibe fundo e paredes de rocha, com o piso preservado em −2 m. Salas inteiramente subterrâneas mantêm teto. A trilha, a grama e as rochas ganham textura legível. O espalhamento automático respeita a área escavada após carga inicial e replay. Validar sem gravar no mundo compartilhado.

1. [concluído] Reproduzir o disco cinza com o journal de produção e registrar a causa.
2. [concluído] Corrigir geometria e materiais, detalhar superfícies e integrar o espalhamento.
3. [concluído] Comparar capturas e validar escavação, replay, sala subterrânea e build Release.
4. [em andamento] Publicar na main, confirmar o resultado servido no GitHub Pages e converter este plano em atividade.

## Referências e decisões visuais

- Captura do mantenedor: o fundo do poço precisa ficar legível e a árvore que cobre a escavação precisa sair do espalhamento natural.
- Cores do próprio Ultima: preservar verde da clareira e terra quente da trilha. Acrescentar variação fina em coordenadas do mundo, com rocha acinzentada e estratos na escavação.
- Textura procedural dos pedregulhos e pipeline emissivo do terreno: reutilizar o padrão de rocha no mundo e nos pedregulhos. A geometria e as texturas procedurais atendem ao desenho e rodam nos dois modos do motor.
- Direção da Refero: usar a captura e as superfícies do jogo como alvos, mantendo a hierarquia visual entre trilha, vegetação, borda e piso.

Reprodução: journal de produção em sessão isolada, piso em −2 m, raio vertical atinge carve-wall a 0,30 m e a tampa só usa cor plana. A cena também tem instâncias naturais dentro do poço.

A correção remove o cilindro do poço aberto, conserva teto texturizado para salas subterrâneas e atualiza o espalhamento natural na carga e no replay. Paleta, granulação do solo e textura de rocha ficam em materiais procedurais.

Validação: captura do poço com piso e paredes visíveis. Chromium com WebGL2 passou no worker e na main. Raio encontra a malha em −2 m, com textura de 256 px e sem vegetação automática no poço. Sala subterrânea, evento de escavação e replay vazio passaram. node --check, testes Node, RulesSmoke, git diff --check e publish Release passaram.

Próxima ação: publicar e repetir a verificação no site servido pelo Pages.
