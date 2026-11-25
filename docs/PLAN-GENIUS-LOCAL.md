# Genius incorporado ao Ultima

O Ultima inicializa uma instância headless do Genius sob demanda no
Blazor WebAssembly, como serviço do jogo. A interface continua sendo a
conversa da Guardiã. A experiência do CLI vem do mesmo runtime de sessões
do Genius. O jogo carrega o runtime junto dos seus módulos WebAssembly.

1. [concluído] Validar o runtime headless do Genius no Chromium,
   recebendo o motor de inferência e o armazenamento do jogo.
2. [parcialmente bloqueado] Definir a distribuição do componente para o Ultima público.
   A prova com o Core inteiro mediu 1,61 MB em Brotli. A distribuição
   pública do componente depende de autorização. Medir o recorte final
   e o impacto sobre o tempo de quadro antes de colocá-lo no jogo.
3. [pendente] Integrar a Guardiã ao serviço do jogo com os modelos WebLLM
   e wllama existentes. Persistir sessões no IndexedDB, separadas por
   mundo, jogador e NPC. Manter turnos locais fora da telemetria.
4. [pendente] Registrar skills e ferramentas por família da API. Validar
   permissões, aplicação de edições e idempotência no jardim isolado.

A página inicia o serviço ao primeiro uso. A inferência recebe o histórico
do Genius e roda na CPU/GPU do jogador. O Ultima verifica permissões e
aplica efeitos através da API do jogo. O navegador mantém os dados locais
e o modo hospedado requer escolha explícita.

Validação do jogo: `dotnet publish src/Ultima.csproj -c Release`, cenários de
diálogo e serviços em WorldCollisionE2E, teste de CPU e GPU e verificação do
hash publicado no GitHub Pages. Use o diálogo atual e os componentes da
Sufficit.Blazor.UI.

O protótipo Release rodou em Chromium com trimming e retomou o histórico
após recarregar a página, com inferência simulada e armazenamento local.
Os motores WebLLM/wllama e o IndexedDB pertencem à integração do jogo.
