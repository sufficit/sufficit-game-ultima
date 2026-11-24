# Auditoria dos planos e recuperação do cache de malhas

## Investigação

Os quatro planos em `docs` tinham checkpoints concluídos e atividades com os
commits correspondentes. As antigas seções de próxima ação do worker e dos NPCs
já tinham sido executadas. A fila amortizada e o diálogo baseado em decisões
seguem no cliente.

A migração da malha para C# deixou gravação e leitura do OPFS com chaves
diferentes. A geração no Babylon.js também deixava de consumir o resultado da
leitura. O teste de revisita reproduziu a falha no build `781180f`.

## Alterações

- O cache armazena o bloco binário de malha, incluindo índices e coordenadas de
  textura. A chave inclui o build e as coordenadas do chunk. A leitura confere
  formato, tamanho e SHA-256 do bloco antes de reutilizá-lo.
- A leitura ocorre fora do quadro e tem prazo de 250 ms para enfileirar o
  fallback. Um bloco que termina de carregar antes da geração ainda pode ser
  aproveitado. Malhas editadas são sempre calculadas do estado corrente.
- O índice aponta para as quatro atividades. Cada atividade preserva o link
  permanente para o plano original e seus checkpoints.

## Validação e entrega

- `dotnet publish src/Ultima.csproj -c Release` e 100 verificações de
  `RulesSmoke`.
- Chromium com SwiftShader, em worker e fallback. Visita fria gera e persiste
  a malha. Revisita usa OPFS. Escultura ignora o cache base. Dano no bloco força
  regeneração com a mesma geometria. A fila carregou os seis modelos previstos.
- Emendas e câmera com streaming exercitadas nos dois modos. Diário, diálogo
  e aprovação da proposta da Guardiã exercitados no worker.
- A validação histórica de GPU nos documentos originais mantém as ressalvas
  de hardware. Estes testes usaram renderização por software.
  [Índice das atividades](../README.md) · [Arquitetura](../../ARCHITECTURE.md)
