# Migração dos testes de integração do servidor para C#

## Investigação

Três scripts Python em `tests/` (`world-store`, `garden` e `diagnostics`)
subiam o servidor Release e exercitavam a API por HTTP. Antes da migração, a
linha de base mostrou instabilidade: em 10 execuções do `world-store` passaram 5;
em 12 do `garden`, 2. Duas causas estavam no servidor:

- A semente da cristalização de chunk vinha de `HashCode.Combine`, que muda a
  cada processo do .NET. O mesmo chunk virava clareira numa inicialização e
  ficava vazio na seguinte, contrariando a decisão `world-builder-v5` (mesmo
  chunk, mesmo mundo).
- No replay do journal, as edições gravadas diretamente (a cristalização)
  voltavam com a chave `op`, o número da operação, que não existe no estado em
  memória. O mapa servido depois de reiniciar não era igual ao de antes.

A terceira causa era dos scripts: a cristalização roda no tick de fundo, depois
da primeira sessão, e eles comparavam o mapa inteiro antes e depois dela.

## Alterações

- [tests/ServerIntegration](../../tests/ServerIntegration): suíte em C#, sem
  pacotes externos, com os cenários `world`, `assets`, `garden` e
  `diagnostics`. Sobe o servidor Release em porta efêmera e diretório de dados
  temporário; o provedor de IA fictício (formato OpenAI) é um `HttpListener`
  local. As comparações de mapa usam só o que pertence aos jogadores: células,
  edições com dono e colocações que não vêm da cristalização.
- [app/WorldEdits.cs](../../app/WorldEdits.cs): semente do chunk por FNV-1a de
  `ultima:cx,cz`, igual em qualquer processo. Chunks já visitados continuam
  como o journal gravou; a semente só decide a primeira visita de chunks novos.
- [app/WorldStore.cs](../../app/WorldStore.cs): o replay remove a chave `op`
  das edições de superfície lidas do journal.
- Os scripts Python saíram do repositório. A [validação do
  mundo](../USAGE-MUNDO.md) e as atividades que apontavam para eles foram
  atualizadas.

## Validação e entrega

- Linha de base em Python, antes das correções: `world-store` 5 de 10;
  `garden` 2 de 12.
- Depois das correções, a suíte C# passou 17 execuções seguidas com os quatro
  cenários: `world` 0,45–0,74 s, `assets` 0,53–0,78 s, `garden` 0,46–0,63 s e
  `diagnostics` 0,18–0,29 s, cerca de 2,2–2,5 s por execução.
- Tempo: os scripts Python levavam 1,4 s (`world-store`) e 0,8 s (`garden`)
  quando passavam. O tempo é dominado pela subida do servidor, não pela
  linguagem: a migração não deixou o teste mais rápido. O ganho foi tirar a
  dependência de Python, comparar por valor com evidência na falha e revelar as
  duas falhas do servidor acima.
- Build Release do servidor e publish Release do cliente concluíram sem erro.
