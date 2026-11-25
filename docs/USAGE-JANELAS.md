# Layout das janelas do Ultima

As definições estão em `src/Windows/Layouts/`. O build as inclui no assembly
do Blazor. Cada arquivo tem `version: 1`, `title` e uma lista `controls`.
A ordem dos controles é a ordem de leitura. O Razor desenha os controles
interativos com Sufficit.Blazor.UI.

| Controle | Propriedades |
| --- | --- |
| `transcript` | Histórico do NPC no Diário local e falas da conversa aberta, em ordem cronológica. |
| `note`, `status` | `text` fixo ou `bind` de texto. `note` aceita o estilo `error`; `status` anuncia o resultado. |
| `row` | `children` dentro de uma linha responsiva. |
| `form` | `action` de envio e `children`. |
| `input` | `id`, `label`, `bind`, `text` como placeholder e `maxLength`. |
| `submit` | `text`, `label` e `icon`. Aciona o formulário pai. |
| `button` | `text` ou `bind`, `action`, `icon` e estilo opcional `outline`. |
| `list` | `bind` de uma lista de textos. |

`when` pode apontar para um campo de estado. O controle aparece quando
o valor é preenchido. Ligações ausentes produzem texto ou lista vazia.
As ações das duas janelas entram no mesmo dispatcher do jogo. O tema é
aplicado à janela em Razor e usa os tokens SUI; o JSON descreve a estrutura.

Exemplos em uso no jogo. [Conversa](../src/Windows/Layouts/conversation.json)
· [Diário](../src/Windows/Layouts/journal.json).

## Relato de erro

**Reportar erro** abre uma descrição opcional na conversa. O jogo envia
`POST /v1/diagnostics/reports` com a versão, a renderização, o modelo, o
estado, o progresso e a latência disponível dos motores CPU e GPU. A
resposta `201` contém um `id` de protocolo. O histórico e os tokens
ficam fora do pedido.

A API grava registros JSONL em `diagnostic-reports.jsonl` sob
`ULTIMA_DATA_DIR`, com uma geração anterior na rotação. A operação pode
localizar o relato pelo ID nos arquivos do volume. A rota limita o corpo
a 4 KiB e devolve `429` ao atingir o limite de frequência.
