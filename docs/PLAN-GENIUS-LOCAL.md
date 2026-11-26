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
4. [em andamento] Validar o build, a conversa e a retomada no Chromium. Verificar a
   presença da versão no GitHub Pages.
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

## Execution checkpoints

1. [complete] Probe real CPU and GPU inference and reload continuity in Chromium.
2. [complete] Add the Characters API family for reading and partially updating
   an owned profile. Load the persisted profile in both environments.
3. [complete] Validate authorization, persistence, and both renderer modes.
4. [in progress] Deliver the verified changes and preserve any remaining Genius checkpoints.

The real inference probes run during API implementation. Their results remain
required for the original inference validation checkpoint.

The body profile is edited through the API. Future NPCs use the same
owner-scoped operation. The client applies the saved profile at scene startup.

The [Characters API](USAGE-API-CHARACTERS.md) and its
[repository skill](../.agents/skills/ultima-characters/SKILL.md) cover the first
new family. The isolated server tests verified ownership, idempotency, and
recovery after restart. Chromium loaded the saved profile in the garden and
office in both renderer modes. Registering callable NPC tools remains open.
