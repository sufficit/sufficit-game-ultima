# Plano: IA determinística de criaturas (MobBrain)

**Estado:** proposta revisada em 24/11/2026 — checkpoint 1 (mapeamento do código)
concluído; nenhuma implementação de mobs incluída.

## Objetivo

Planejar a primeira fatia de IA para criaturas hostis do Ultima, com decisões
previsíveis, testáveis e baratas por atualização. O objetivo não é introduzir IA
generativa: é dar aos mobs comportamentos legíveis (perceber, perseguir, atacar e
retornar) e permitir evoluí-los sem espalhar condicionais pela lógica do mundo.

## Contexto e premissas (atualizado em 24/11/2026)

- A arquitetura vigente é a descrita em `ARCHITECTURE.md`: o C# já simula o
  jogador, o Andarilho, os avatares remotos e o terreno em WebAssembly, enquanto o
  Babylon.js desenha a cena e consulta as malhas. Existe um backend opcional
  (ASP.NET Core) que atua como autoridade do mundo compartilhado — o cliente
  inicia e roda sem ele. (`PROJECT.md` ainda descreve o M1 e não acompanhou as
  entregas posteriores.)
- Movimento e colisão **existem** em C# (detalhe no checkpoint 1). **Não existem**:
  entidades de mob, vida/dano/combate (nenhuma ocorrência em `engine/`) e
  navegação por caminho (a patrulha do Andarilho é circular e, ao bloquear,
  congela a fase da patrulha em vez de contornar o obstáculo).
- `src/Npc` (cérebros de diálogo decidindo sobre `WorldSnapshot` imutável,
  `NpcScheduler` com orçamento por tick) é o **padrão de desenho** a reutilizar.
  O scheduler em si não serve para mobs: é orientado a eventos de diálogo e roda a
  5 Hz na thread do Blazor (`MoveTick` em `src/Pages/Index.razor`), sem acesso aos
  colisores e sem cadência por quadro.
- `lineage-rust` é referência de padrões de um bot cliente, não código a copiar;
  verificar licenças antes de qualquer reutilização.
- Fora deste plano: LLM, sincronização do mob no mundo compartilhado (autoridade
  servidor × host), persistência e API remota. No primeiro marco cada cliente
  simula o seu próprio mob de teste; jogadores conectados ao mesmo mundo verão
  mobs independentes. Isso é aceitável para validar a lógica e vira um plano
  próprio antes de qualquer mob "real" no mundo compartilhado.

## Valor pretendido

- **Comportamento observável:** o mob reage à proximidade do jogador, persegue
  dentro de limites claros e desiste/retorna ao território ao perder o alvo.
- **Iteração de conteúdo:** regras e parâmetros ficam separados da atualização do
  mundo; variantes futuras são configuração, não cópias de árvores de `if`.
- **Confiabilidade:** decisão pura sobre snapshot, testável sem renderer, navegador
  ou rede; cadência limitada evita trabalho por quadro.

## O que o código já tem (checkpoint 1)

| Área | Onde | O que aproveitar |
| --- | --- | --- |
| Movimento e colisão | `engine/Ultima.Terrain/PlayerMovement.cs` | Varredura contra colisores cilíndricos vindos do Babylon (desliza em até 3 passadas, limite do mundo ±400 m, passo sobre obstáculos baixos). Andar 3,5 m/s, correr 6,5 m/s, raio 0,25 m. |
| Contato corpo a corpo | `engine/Ultima.Terrain/PlayerCharacterSpace.cs` | Caixas orientadas por malha; ids fixos `Player=-1`, `Guardian=-2`, `Wanderer=-3`, avatares remotos ≥ 0. `Move(id, …)` devolve a posição permitida e `Blocked`. |
| Contrato por quadro | `engine/Ultima.Terrain/BrowserExports.Player.cs` | `AnimateCompanion` (um ator: o JS envia posição/origem/orientação e recebe 29 valores) e `UpdateRemoteAvatars` (lote: todos os avatares em uma chamada por quadro). São os moldes do contrato do mob. |
| Cérebros sobre snapshot | `src/Npc/WorldSnapshot.cs`, `DialogBrain.cs`, `NpcScheduler.cs` | Snapshot imutável, decisão → comandos, orçamento por tick (4 eventos/16 comandos, fila 32). |
| Regras D&D | `src/Rules/D20.cs`, `Dice.cs`, `Character.cs` | `D20.Test(kind: Attack)` com RNG semeado. Hoje só o `RulesSmoke` as exercita; o jogo não as referencia e o módulo WASM do worker não as compila. |
| Testes | `tests/RulesSmoke`, `tests/*.e2e.cjs` | `RulesSmoke` compila por link `src/Rules/*.cs`, `src/Npc/*.cs`, `Terrain*.cs`, `Player*.cs` e `World*.cs`; os E2E rodam contra build local e site publicado, nos modos worker e `?render=main`. |

O loop: `game-core.js` (compartilhado pelo worker e pela thread principal) chama o
WASM a cada quadro — `MovePlayer` para o jogador, `AnimateCompanion` para o
Andarilho, `UpdateRemoteAvatars` para a rede. `TerrainCrowd` só faz descarte e
nível de detalhe de figuras paradas.

## Arquitetura decidida

- **Onde roda:** o cérebro é C# puro em `engine/Ultima.Terrain/`, em arquivos
  `Mob*.cs`. O módulo WASM do worker compila a pasta inteira; o Blazor do modo
  principal e o `RulesSmoke` só compilam o que os links dos `.csproj` casam, então
  os dois ganham um `<Compile Include>` para `Mob*.cs` — o mesmo esquema do relógio
  e do clima (`World*.cs`). Sem o link, o mob existiria só no worker e o modo
  principal quebraria. A aplicação das decisões fica em
  `BrowserExports.Player.cs`, ao lado do Andarilho. Nada de lógica de mob em
  JavaScript.
- **Decisão pura:** `MobBrain.Decide(MobSnapshot) → MobAction`. O snapshot é
  imutável: posição e orientação do mob, ponto de origem, estado atual, vida,
  posição do jogador **ou** "jogador desconhecido" (a posição chega pelo contrato,
  como em `UpdateRemoteAvatars`), recarga do ataque e o tempo decorrido. O cérebro
  não lê nem altera o mundo.
- **Prioridade fixa, uma ação por decisão:** `morto` → `retornar` (fora do
  território ou perseguição excedeu o limite) → `atacar` (alvo conhecido, ao
  alcance e com o ataque pronto) → `perseguir` (alvo percebido) → `ocioso`.
  Jogador desconhecido nunca produz ataque nem perseguição.
- **Aplicação por quadro, em lote:** `StepMobs` recebe todos os mobs de uma vez e
  devolve, para cada um, posição, orientação, estado e o evento de acerto do
  quadro — uma chamada por quadro, não uma por mob. Para cada mob, o C# chama o
  cérebro quando o passo vence e move o resultado pelo `PlayerCharacterSpace`
  (corpos) e pela varredura de obstáculos do jogador (`PlayerMovement.Sweep`,
  raio de 0,25 m): o mob não atravessa obstáculos por decisão de IA. O estado de
  comportamento (fase, recarga, origem) mora no C#; o JavaScript guarda só o
  transform, como já faz com o Andarilho.
- **Cadência e orçamento:** decisão em passo fixo de 100 ms com acumulador; o
  movimento é aplicado a cada quadro para manter deslizamento e colisão suaves.
  No máximo 16 decisões por passo (excedente fica para o passo seguinte). Mob a
  mais de 40 m do jogador não decide: um retorno em curso continua, uma
  perseguição vira retorno e um mob ocioso fica parado.
- **Parâmetros padrão** (em um único record): raio de percepção 8 m, alcance de
  ataque 1,6 m, recarga 1,2 s, velocidade de perseguição 3,0 m/s (o jogador anda a
  3,5 e corre a 6,5 — fugir andando abre vantagem aos poucos; correndo, rápido),
  limite de retorno 14 m da origem, desistência após 3 s sem alvo conhecido.
- **Sem linguagem de script nem editor visual.** Configuração serializada só
  depois de haver mais de um comportamento concreto e necessidade comprovada.

## Primeiro marco: um mob, três regras

Um único mob de teste (marcador simples ou o pacote `character.wanderer` como
emprestado), habilitado por `?mob=1`, com três regras: (1) perceber o jogador
dentro do raio; (2) perseguir e atacar somente dentro do alcance e da recarga;
(3) ao exceder o limite de retorno ou perder o alvo, regressar à origem e
abandonar a perseguição. O reinício do mob sem recarregar a página usa um comando
de teste no estilo do teleporte que os E2E já usam — sem controle novo na
interface (se houver, é Sufficit.Blazor.UI).

Fora do primeiro marco: mais de um mob, vida e dano, campo de visão, animação de
ataque, surgimento e espólios.

## Riscos e mitigação

- **Mob preso ao retornar** (não há navegação por caminho): a varredura desliza
  pelo obstáculo; se ficar preso por mais de 2 s, conclui o retorno por
  reposicionamento na origem e registra o evento. Navegação por caminho é plano
  próprio.
- **Custo com muitos mobs:** contrato em lote, orçamento por passo e medição com
  1 e com 16 mobs antes de fechar.
- **Divergência entre worker e modo principal:** o mesmo código C# nos dois, com
  E2E nos dois modos.
- **Mundo compartilhado:** fora do escopo (decisão 3 abaixo).

## Decisões em aberto

1. **O que é um acerto no primeiro marco.** Recomendado: o ataque produz um evento
   de acerto que o jogo transforma em feedback (clarão no mob e tropeço leve do
   jogador por `PlayerMovement.Trip`), sem pontos de vida. Vida e dano pela regra
   d20 de `src/Rules` entram no segundo marco, com RNG semeado — os dados ficam na
   aplicação, nunca dentro do cérebro, para manter a decisão determinística.
   Hoje `src/Rules` não é compilado no módulo WASM do worker (só no Blazor e no
   `RulesSmoke`); usar d20 no mob exige ligar esses arquivos ao
   `engine/Ultima.Terrain.csproj` ou resolver a rolagem na camada Blazor e passar
   o resultado para o motor.
2. **Percepção.** Recomendado: só raio horizontal. Campo de visão/linha de visão
   só se o teste mostrar que o mob "sente através de paredes".
3. **Mundo compartilhado.** Fica explicitamente fora; exige plano próprio
   (autoridade servidor ou host) antes de qualquer mob sincronizado.

## Critérios de aceite

- Decisão determinística, sem mutação do mundo durante a avaliação do snapshot.
- `RulesSmoke` cobre percepção, perseguição, alcance/recarga, limite de
  perseguição, retorno, estado desconhecido (nunca ataca) e orçamento por passo.
- A integração usa os sistemas de movimento/colisão existentes nos dois modos
  (worker e `?render=main`), sem atravessar obstáculos por decisão de IA e sem
  segunda fonte de verdade para posição/estado.
- E2E no navegador confirma cada transição e o retorno à origem, com o mob
  reiniciável sem recarregar a página.
- Custo de quadro medido antes e depois (mediana e p95) em cena equivalente, com
  1 e com 16 mobs; console sem erros.
- `dotnet publish -c Release` sem erro; publicado no Pages e validado ao vivo em
  https://sufficit.github.io/sufficit-game-ultima/ antes do fechamento.
- Nenhum comportamento exige API ou conexão. Se o movimento não sustentar o
  retorno confiável, documentar o bloqueio e não marcar o marco como funcional.

## Checkpoints

1. [concluído] Mapear `src/Rules`, `src/Npc`, o loop de `game-core.js`, o contrato
   de movimento/colisão e os testes. Resultado na tabela acima: movimento e
   colisão são C# no WASM; não há entidades de mob nem combate; o scheduler de
   diálogo não é o lugar do mob; a fronteira C#/JavaScript é o contrato por quadro
   de `BrowserExports.Player.cs`; o cérebro vai em `Mob*.cs`, com link nos dois
   `.csproj` como `World*.cs`.
2. [pendente] Modelar `MobSnapshot`, `MobAction`, parâmetros e o `MobBrain`;
   testes no `RulesSmoke` sem renderer ou rede.
3. [pendente] Aplicar em lote em `BrowserExports.Player.cs` (`StepMobs`, reinício
   do mob), ligar o mob de teste em `game-core.js` (`?mob=1`) nos modos worker e
   principal, com um id próprio no `PlayerCharacterSpace`.
4. [pendente] E2E local, medição de quadro, publicação e validação ao vivo;
   registrar evidências. Concluindo o escopo, converter este plano em atividade
   conforme `AGENTS.md`.

## Próxima ação

Checkpoint 2: modelar snapshot, ação e parâmetros mínimos do mob e escrever os
testes de decisão no `RulesSmoke`. Este documento ainda não autoriza nem registra
implementação de código.
