# t48 — IA da Guardiã na CPU do jogador (engine "cpu-wllama")

Data: 2026-11-22 · Escopo: segundo motor de conversa local, sem WebGPU, ao lado do engine "webgpu" (ai.js/WebLLM).

## Decisão
- Motor: **wllama 3.13.0** (llama.cpp compilado para WASM) carregado por ESM do CDN jsDelivr; binários `.wasm` também do CDN. Zero dependências no repo.
- Single-thread forçado (`n_threads: 1`, `n_gpu_layers: 0`): GitHub Pages **não envia COOP/COEP**, então `SharedArrayBuffer` não existe e multi-thread não roda. Avaliar `coi-serviceworker` ficou como **pendência** (ganharia ~2-4× em troca de um service worker a manter).
- Cadeia de resposta: WebGPU (se ativo) → **wllama CPU** → regras/API do servidor. Origem marcada em cada resposta; `ActiveEngine()` visível no painel.

## Modelos (dois, propostas diferentes; GGUF por URL do CDN da Hugging Face, CORS confirmado)
| Chave | Modelo | URL | Tamanho | Proposta |
|---|---|---|---|---|
| `gemma270` | Gemma 3 270M IT Q4_K_M | https://huggingface.co/unsloth/gemma-3-270m-it-GGUF/resolve/main/gemma-3-270m-it-Q4_K_M.gguf | 253.115.424 B (~241 MB) | leveza/velocidade máxima em máquinas fracas |
| `qwen05b` | Qwen2.5 0.5B Instruct Q4_K_M | https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf | 491.400.032 B (~469 MB) | melhor qualidade de conversa em português |

- Gemma 3 270M foi encontrado (repo `unsloth`), **sem** necessidade do fallback SmolLM2 360M (o GGUF oficial que testamos devolveu 404).
- Persona curta e fixa embutida (2-4 frases), sem chamar a API do jogo: modelos pequenos se perdem em prompts longos.

## Arquivos
- `src/wwwroot/js/ai-cpu.js` (novo): engine `cpu-wllama`, watchdog de travamento, velocidade de download, tok/s, histórico curto, `setModel`.
- `src/Pages/Index.razor`: painel "IA na CPU" com seletor de modelos, progresso, estados e motor ativo; cadeia de fallback em `AskGuardianAsync`.
- `src/GAME.JsInterop.cs`: callback `OnAiCpuState`.
- `src/wwwroot/index.html`: inclui `ai-cpu.js`.

## Prova real (2026-11-22, chromium headless 1255, 480x360, 1 thread)
- **Gemma 3 270M (`gemma270`): APROVADO.** Pergunta "Quem é você?" → resposta real: "A Guardiã do Jardim do jogo Ultima: serena, acolhedora, sábia." Download 241 MB a ~36,7 MB/s; carga+resposta em ~47 s; ~1,1 tok/s; 0 erros de console; CORS da HF ok.
- **Qwen 0.5B (`qwen05b`): não validado neste ambiente.** 3 execuções mortas pelo guardião de memória do contêiner (exit 137, working set ≥ 92% do teto de 2 GB) — o GGUF de 469 MB + heap WASM não cabe no headroom deste executor. O modelo continua ofertado no painel para máquinas clientes com mais RAM; pendente validação em desktop.
- Build Release pós-merge: sucesso, 0 erros. Merge `3f78cde` trouxe o cache OPFS de outra frente sem conflito.
- Critério de publicação ("sem resposta comprovada, não publicar") cumprido pelo motor `cpu-wllama` com o modelo padrão `gemma270`.

## Publicação e validação ao vivo (2026-11-22)
- Commit único `84d2e9e` no `main` via API do GitHub (árvore `2f968d1`, pai `fa92baf`), após merges de 3 frentes concorrentes (cache OPFS, assets GLB, docs IA NPCs) — nenhum trabalho do remoto revertido; build Release da árvore final: 0 erros/0 avisos.
- Workflow **Pages 44967647189: success** (build + deploy) no commit `84d2e9e`.
- Artefatos ao vivo em `https://sufficit.github.io/sufficit-game-ultima/`: index/ai-cpu.js/extras.css = 200; HTML referencia `ai-cpu.js?v=84d2e9e…` (cache-busting); SHA-256 do `ai-cpu.js` ao vivo idêntico ao do repo; smoke test: engine `cpu-wllama` presente, zero erros de console.
- **Fase A — prova ao vivo com o artefato publicado (página mínima carregando o `ai-cpu.js` real do github.io): APROVADA.** `gemma270` carregou em 22,5 s e respondeu "Quem é você?" em 27,4 s (~50 s total): *"Olá! Sou a Guardiã do Jardim do jogo Ultima, um especialista em ajudar a gente a se divertir e a encontrar a paz."*
- Nota de ambiente: as quedas dos testes anteriores (Qwen 3× exit 137; primeiro teste ao vivo sem resultado) foram do executor de validação — guardião de memória do contêiner, timeout meu de 300 s e servidor de teste morto junto com a árvore do comando — nenhuma do código publicado.
