# Conflitos de movimento no harness mobile

## Investigação

O navegador registra no console os conflitos HTTP da API, inclusive quando o jogo trata a resposta. A rota de comandos recebe várias ações. Suprimir todo 409 nessa rota ocultava falhas de outras operações.

## Alterações

O observador em `tests/game-command-conflicts.cjs` correlaciona a mensagem do Chromium com a resposta na mesma URL. Cada POST de ação `pose` que recebe 409 e o erro `too_fast` justifica uma ocorrência no console. Respostas de conflito inesperadas também falham quando o Chromium omite a mensagem. Os testes de regressão exercitam a contagem, o método, a ação e o motivo.
Em 25/11 o observador em node deu lugar a `CommandConflicts` na suíte de navegador
em C#, com os mesmos casos no `SelfCheck`
([atividade](202611251151-testes-mobile-e-dialogo-csharp.md)).

## Validação e entrega

O workspace do Genius recebeu a integração no teste de retrato e desktop (commit local `8531043`). O harness completou as 36 verificações em Standard, com prazo de 600 segundos. Houve uma ocorrência confirmada de `pose/too_fast` no retrato. O repositório público contém o observador e seus testes isolados. A suíte específica passou nos quatro casos e a publicação local em Release concluiu. [Jogo em produção](https://sufficit.github.io/sufficit-game-ultima/).
