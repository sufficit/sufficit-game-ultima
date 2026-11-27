# Guia do jogador

Abra o [Ultima](https://sufficit.github.io/sufficit-game-ultima/) no navegador.
Explore a clareira e siga as dicas de interação que aparecem quando você se
aproxima de um personagem ou objeto.

## Controles

| Ação | Teclado e mouse | Controle | Toque |
| --- | --- | --- | --- |
| Andar | WASD | Analógico esquerdo ou direcional | Analógico esquerdo na tela |
| Correr | Shift enquanto anda | | |
| Ajustar a câmera | Setas direcionais ou arrastar com o mouse | Analógico direito | Analógico direito na tela |
| Aproximar ou afastar | Roda do mouse | | |
| Conversar ou examinar | E | A / ✕ | Botão A |
| Fechar o diálogo | Esc | B / ○ | Botão B |

## Camera views

Press **V**, click the mouse wheel, press the controller's right stick
(**R3**), or use **Painel → Alternar câmera** to cycle through the views.

- **Elevated orbit** follows the current character from above for exploration
  and a wider view of the surroundings.
- **First person** puts the camera at the current character's eye level.
- **Close third person** follows behind the character. Move the mouse to aim
  and scroll to adjust the distance from **1.85 m to 3.80 m**. The default is
  **2.15 m**. **Esc** releases the pointer. Click the scene or cycle back to
  the close view to resume. An obstacle can bring the camera closer.

Current controls apply to one character at a time. The planned group mode
uses the elevated view to frame and command up to five characters. The two
closer views focus on the active member for direct control.
[Camera framing and control scope](../decisions/camera-framing-and-control-scope.md)

As dicas de interação acompanham o último controle usado. Ao mudar de aba ou
focar um campo de texto, as teclas de movimento são soltas. Feche um diálogo
com Esc ou B mesmo com o campo de mensagem em foco.

A câmera acompanha a altura do terreno e recua quando um obstáculo fica
entre ela e o personagem. Use a órbita para olhar em volta em um caminho
estreito ou em um túnel.

## Coletar e modificar o jardim

1. Abra **Painel → Mochila → Coletar**.
2. Aponte para um objeto ou trecho de solo próximo. O destaque indica o alvo.
   Clique ou toque para coletar.
3. Consulte os recursos acumulados na **Mochila**. Use **Parar coleta** para
   voltar à exploração.

A **Oficina do mundo** reúne ferramentas para escavar, pintar e posicionar
objetos. Para gravar uma edição no mapa compartilhado, conecte-se à API
do jogo. [Edições do mundo](USAGE-MUNDO.md)

## Conversar com a Guardiã

Approach the Guardian and use the conversation action. Enter what you want
to discuss or change. The window supports typing, dictation, and read-aloud
where the browser supports them. The Journal keeps earlier replies.

- Enable a supported AI model to converse in your browser. The first use may
  download model weights.
- The Guardian can inspect placed objects and propose changes when connected
  to the shared world. Review the proposal before confirming the edit.
- The `/lembrar` and `/memórias` commands manage local notes.
  [Guardian memory](USAGE-API-JARDIM.md#memória-da-guardiã-no-navegador)
- Use **Reportar erro** to send a diagnostic. The game displays a report ID
  when the server accepts it. [Diagnostic data](USAGE-JANELAS.md#relato-de-erro)

## Jogar junto ou explorar sozinho

No **Painel → Multiplayer e voz**, use **Copiar convite** para compartilhar
o endereço da sala. Os jogadores que abrem o mesmo convite podem se encontrar
e conversar. Ative o microfone no painel para usar a voz.

Para explorar com o mundo compartilhado e o multiplayer desativados,
abra o jogo com `?live=0&rede=0`. A mochila e o histórico local ficam no
navegador. A conexão pode ser necessária para carregar os arquivos do jogo e
dos modelos de IA.

Em **Painel → Tempo e clima**, veja a hora GMT e a hora local, escolha o fuso
e ajuste o clima da sua cena. [Relógio, céu e clima](USAGE-MUNDO.md#relógio-céu-e-clima)
