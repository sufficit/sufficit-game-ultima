# Instruções do agente — Ultima (sufficit-game-ultima)

Jogo open source em C# / Blazor WebAssembly com mundo 3D em Babylon.js
(WebGL/WebGPU), sem servidor de jogo. Contexto completo em
[PROJECT.md](PROJECT.md); mapa do repositório no [README.md](README.md).

## Build e validação

- SDK .NET 10; `dotnet publish src/Ultima.csproj -c Release` tem que passar
  sem erro antes de qualquer checkpoint de código.
- Publicação é automática no push da `main` (GitHub Pages). O jogador não
  acessa previews locais: **validar sempre o site ao vivo**
  https://sufficit.github.io/sufficit-game-ultima/ e reportar só URLs de
  produção; push não comprova deploy.
- DLLs de modelos (`models/`) têm SHA-256 verificado em runtime e recusa com
  fallback para marcadores; nunca enfraquecer essa verificação.
- Tests em `tests/` (ex.: `tests/RulesSmoke`): rodar os relevantes ao escopo.

## Planos e atividades

- Planos de trabalho vivem em `docs/PLAN-*.md`, um arquivo por frente; índice
  em [docs/README.md](docs/README.md). Cada plano traz objetivo/critérios de
  aceite, checkpoints numerados com estado (`[pendente]`, `[parcialmente
  bloqueado]`, `[concluído]`) e uma próxima ação.
- **Um plano permanece aberto enquanto tiver pendências** — atualize os
  checkpoints nele a cada progresso; não abra plano novo para trabalho que já
  tem plano.
- **Ao concluir o escopo inteiro de um plano, convertê-lo em documento de
  atividade** em `docs/activities/AAAAMMDDHHMM-slug.md` (fuso
  America/Sao_Paulo), preservando as evidências e corrigindo os links: o plano
  sai do índice de ativos e o `docs/README.md` passa a apontar para a
  atividade. Não encerrar trabalho de outra sessão.
- Formato da atividade: título descritivo e seções **Investigação**,
  **Alterações** e **Validação e entrega** (o que foi feito, o que mudou, e a
  evidência: testes, medidas, commit/URL de produção validada).

## Regras do projeto

- Falar português nos documentos e nos commits; commits na `main` com escopo
  pequeno e mensagem descritiva.
- Decisões de design históricas em `decisions/`; não reabrir decisão registrada
  sem registrar a nova.
- `app/` (API opcional em ASP.NET Core; a implementação anterior em Python
  fica em `app-python/`) é fallback-friendly: o jogo nunca pode depender
  dela para iniciar.
- Priorizar o tempo de quadro nas migrações para C#. Agrupar chamadas na ponte
  JavaScript–WASM e comparar o custo antes e depois em cena equivalente.
  Validar worker e fallback conforme a [arquitetura](ARCHITECTURE.md).
- **Interface do jogo = Sufficit.Blazor.UI.** Painéis, botões, campos, seletores e
  interruptores usam os componentes da biblioteca (versão fixada no `.csproj`), nunca
  HTML cru estilizado à mão; o estado dos controles mora no Blazor e chega ao motor por
  JS interop (o JavaScript não lê nem altera o DOM dos controles). Padrão de referência:
  a Oficina do mundo e o Painel do jogo em `src/Pages/Index.razor`; motivação e limites
  em `docs/activities/202611230037-menus-sufficit-blazor-ui.md`. O parâmetro `Subtitle`
  dos painéis de expansão cola título e subtítulo nesta versão: não usar.
- Aproximação técnica do Ultima sempre publicável sem confirmação extra
  (decisão do mantenedor), desde que validada no site de produção.
