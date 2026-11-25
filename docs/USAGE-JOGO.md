# Guia do jogador

Abra o [Ultima](https://sufficit.github.io/sufficit-game-ultima/) no navegador.
Explore a clareira e siga as dicas de interação que aparecem quando você se
aproxima de um personagem ou objeto.

## Controles

| Ação | Teclado e mouse | Controle | Toque |
| --- | --- | --- | --- |
| Andar | WASD ou setas | Analógico esquerdo ou direcional | Analógico esquerdo na tela |
| Correr | Shift enquanto anda | | |
| Girar a câmera | Arrastar com o mouse | Analógico direito | Analógico direito na tela |
| Aproximar ou afastar | Roda do mouse | | |
| Conversar ou examinar | E | A / ✕ | Botão A |
| Fechar o diálogo | Esc | B / ○ | Botão B |

Press **V**, click the mouse wheel, press the controller's right stick
(**R3**), or use **Painel → Alternar câmera** to cycle through rear third
person, first person, and front third person.

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

Aproxime-se da Guardiã e use a ação de conversar. A janela mostra a conversa e
oferece caixa de texto, ditado e leitura em voz alta, conforme o suporte do
navegador. O **Diário** reúne as respostas anteriores.

- Para conversar com um modelo no próprio navegador, ative a IA na CPU ou
  na GPU e use **Testar IA** quando o modelo estiver pronto. Se WebGPU estiver
  indisponível, use a CPU. Os pesos do modelo podem precisar ser baixados.
- Use **Iluminar o jardim** na conversa para pedir luminárias na entrada.
  Outras ações da Guardiã, como plantar e ajustar objetos, dependem do
  provedor de IA e da sessão no mundo compartilhado.
- Para guardar uma nota para consultas futuras, envie
  `/lembrar minha flor favorita é a orquídea`. Envie `/memórias flor`
  para recuperar notas relacionadas. [Memória da Guardiã](USAGE-API-JARDIM.md#memória-da-guardiã-no-navegador)
- Se algo falhar, use **Reportar erro**. O jogo mostra um protocolo após
  confirmar o recebimento do diagnóstico. [Dados do diagnóstico](USAGE-JANELAS.md#relato-de-erro)

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
