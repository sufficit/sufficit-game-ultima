# Layout das janelas do Ultima

As definições estão em `src/Windows/Layouts/`. O build as inclui no assembly
do Blazor. Cada arquivo tem `version: 1`, `title` e uma lista `controls`.
A ordem dos controles é a ordem de leitura. O Razor desenha os controles
interativos com Sufficit.Blazor.UI.

| Controle | Propriedades |
| --- | --- |
| `speaker`, `line`, `note` | `text` fixo ou `bind` de texto. `note` aceita o estilo `error`. |
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
