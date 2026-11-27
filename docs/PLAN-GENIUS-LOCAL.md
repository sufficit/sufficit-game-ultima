# Genius incorporado ao Ultima

O Ultima inicializa uma instância headless do Genius sob demanda no
Blazor WebAssembly, como serviço do jogo. A interface continua sendo a
conversa da Guardiã. A experiência do CLI vem do mesmo runtime de sessões
do Genius. O jogo carrega o runtime junto dos seus módulos WebAssembly.

1. [concluído] Validar o runtime headless do Genius no Chromium,
   recebendo o motor de inferência e o armazenamento do jogo.
2. [concluído] Preparar a biblioteca C# versionada com dependências públicas
   e revisar o conteúdo do pacote. A distribuição do recorte está autorizada.
3. [concluído] Integrar o pacote à Guardiã e aos modelos WebLLM e wllama.
   Persistir sessões no IndexedDB, separadas por mundo, jogador e NPC.
   Manter turnos locais fora da telemetria.
4. [complete] Verify real local inference and history restoration in Chromium and confirm the Pages build.
5. [em andamento] Registrar skills e ferramentas por família da API. Validar
   permissões, aplicação de edições e idempotência no jardim isolado.

A página inicia o serviço de inferência local ao abrir o primeiro turno. A inferência recebe o histórico
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

O piloto `Sufficit.AI.Genius.Embedded` `0.1.0-preview.1` é restaurado do feed
local `pkg/` pelo CI público. A DLL vem do commit `ee98e949` do Genius.
SHA-256 do pacote `04c30fbcb2836f10e8eb48d9367e229f45d031bb4e0d9f84b6c74c6db5769ca3`.
O pacote contém uma DLL e nenhuma dependência NuGet.

O Chromium passou a retomada no IndexedDB ao trocar de CPU para GPU com
modelos simulados. A conversa ficou no Diário local, fora da telemetria.
O teste com a API isolada validou edição, resposta inválida e ausência de
fallback remoto em caso de falha local. O build Release passou com um
cache NuGet novo. A confirmação no Pages e a validação com modelos reais
permanecem evidências distintas.

A entrega do [plugin de memória do navegador](activities/202611251430-plugin-memoria-navegador.md) foi validada no Pages.

## Characters API milestone

The first API family and the real-inference validation are recorded in the
[delivery activity](activities/202611261650-characters-api-and-genius-inference.md). Character edits go through the
API and appear when the scene reloads. Callable NPC tools remain in checkpoint
5. The standalone backend is live on [Sufficit Run](activities/202611262336-characters-api-sufficit-run.md).

The [CPU tool pilot](activities/202611270028-guardian-cpu-tools.md) validates one garden tool with a real local
model and player confirmation. Expansion to other tool families remains in
checkpoint 5.
