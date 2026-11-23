# Plano: IA determinística de criaturas (MobBrain)

**Estado:** proposta — planejamento; nenhuma implementação de mobs incluída.

## Objetivo

Planejar uma primeira fatia de IA para criaturas hostis do Ultima, com decisões
previsíveis, testáveis e baratas por atualização. O objetivo não é introduzir
IA generativa: é dar aos mobs comportamentos legíveis (perceber, perseguir,
atacar e retornar) e permitir evoluí-los sem espalhar condicionais pela lógica
do mundo.

## Contexto e premissas

- O estado atual do projeto descrito em `PROJECT.md` é um jogo de navegador,
  Blazor WebAssembly e Babylon.js, sem servidor central de jogo. Portanto, o
  primeiro marco deve funcionar localmente e não pode depender de backend,
  rede ou serviço externo.
- O projeto já usa decisões calculadas sobre snapshots para NPCs conversáveis
  (`Ultima.Npc`), com scheduler e orçamento por tick. A proposta é reutilizar
  esses princípios, não presumir que o scheduler ou os tipos atuais servem
  diretamente para entidades de combate.
- `lineage-rust` é uma referência de padrões de um bot cliente, não uma
  implementação de IA de monstros. Não copiar código; adaptar conceitos e
  verificar as licenças dos materiais consultados antes de qualquer reutilização.
- Ainda não foi confirmado no código um sistema de entidades de mob, combate,
  navegação ou colisão. O primeiro checkpoint deve mapear essas interfaces e
  ajustar o desenho antes de implementar.

## Valor pretendido

- **Comportamento observável:** um mob reage à proximidade do jogador, persegue
  dentro de limites claros e desiste/retorna ao território ao perder o alvo.
- **Iteração de conteúdo:** regras e parâmetros de comportamento ficam
  separados da atualização do mundo; no futuro, variantes podem ser configuradas
  sem duplicar árvores de `if`.
- **Confiabilidade:** decisões puras sobre um snapshot podem ser testadas sem
  renderer, navegador ou rede; limites de frequência evitam decisões custosas
  a cada frame.

O diferencial é a qualidade do comportamento e a velocidade segura de ajuste,
não a novidade da técnica. Se o jogo ainda não tiver combate ou navegação, o
valor é preparar uma fundação pequena, não prometer mobs inteligentes de imediato.

## Arquitetura proposta (a confirmar no checkpoint 1)

- Separar **observação**, **decisão** e **aplicação**: um snapshot imutável
  contém posição e estado do mob, ponto de origem, vida, alvo visível/distância
  e cooldowns relevantes; o cérebro retorna uma decisão/ação, e o loop existente
  valida e aplica o movimento ou ataque.
- Começar com prioridades determinísticas, uma ação por decisão: `morto` →
  `retornar se fora do território/perseguição excedeu limite` → `atacar se o
  alvo está ao alcance e o ataque está pronto` → `perseguir se o jogador foi
  percebido` → `ocioso`.
- No primeiro protótipo, usar parâmetros explícitos em C# e valores padrão
  seguros. Não criar uma linguagem de script nem editor visual; considerar
  configuração serializada apenas depois de haver mais de um comportamento
  concreto e necessidade comprovada de autoria por dados.
- Executar a decisão em cadência limitada, compatível com a arquitetura atual;
  evitar trabalho por frame e garantir que um mob não bloqueie a simulação.
- Não introduzir LLM, multiplayer, persistência ou API remota neste plano.

## Primeiro marco: um mob, três regras

Demonstrar um único mob de teste com três regras: (1) detectar o jogador dentro
 de um raio; (2) perseguir e atacar somente dentro de alcance/cooldown; (3) ao
 exceder o raio de retorno ou perder o alvo, regressar ao ponto de origem e
 abandonar a perseguição. O visual pode continuar sendo um marcador simples;
este marco valida a lógica, não arte ou animações.

## Critérios de aceite

- Decisão determinística, sem mutação do mundo durante a avaliação do snapshot.
- Testes sem navegador para detecção, perseguição, alcance/cooldown, limite de
  perseguição e retorno; valores ausentes/desconhecidos não provocam ataque.
- Integração usa os sistemas de movimento/colisão existentes, sem atravessar
  obstáculos por uma decisão de IA nem criar uma segunda fonte de verdade para
  posição/vida.
- A cena de teste permite observar as três regras e reiniciar o estado sem
  recarregar a página.
- Build Release e testes relevantes passam; validação interativa em navegador
  confirma cada transição e o retorno ao ponto de origem.
- Nenhum comportamento exige API ou conexão. Se movimento/navegação ainda não
  suportar retorno confiável, documentar o bloqueio e não marcar o marco como
  funcional.

## Checkpoints

1. [pendente] Inspecionar `src/Rules`, o loop `game-core.js`, o contrato de
   movimento/colisão e testes existentes. Escolher o local de execução e a
   fronteira C#/JavaScript que preserve uma única fonte de verdade; atualizar
   este plano com os tipos e arquivos concretos.
2. [pendente] Modelar snapshot, ações e parâmetros mínimos do mob; definir
   comportamento de percepção, alcance, cooldown, raio de retorno e tratamento
   de estado desconhecido.
3. [pendente] Implementar o cérebro determinístico e testes unitários/smoke
   sem dependência de renderer ou rede.
4. [pendente] Conectar um mob de teste ao mundo existente, mantendo a aplicação
   das decisões no loop responsável por movimento e combate.
5. [pendente] Validar build, testes e transições no navegador; registrar
   evidências. Se o escopo todo for concluído, converter este plano em atividade
   conforme `AGENTS.md`.

## Próxima ação

Checkpoint 1: mapear o código atual de NPCs, atualização do mundo, movimento,
colisão e testes antes de decidir a integração. Este documento não autoriza nem
registra implementação de código.
