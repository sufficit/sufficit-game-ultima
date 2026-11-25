# Entrada de teclado, toque e gamepad

1. [concluído] Consolidar a captura do navegador e mover a resolução
   de ações contínuas e a zona morta para C#.
2. [concluído] Validar teclado, toque e gamepad, incluindo foco, escuta de
   botões e soltura. Verificar worker e fallback em Release.
3. [em andamento] Publicar, confirmar a revisão no GitHub Pages e registrar
   o resultado em atividade.

Preservar os atalhos, analógicos, montagem da câmera e formas de interação
do Ultima. A captura DOM e a API Gamepad ficam na ponte. O teste deve usar
o renderizador real em Chromium e dados controlados para o gamepad.

O publish em Release e o RulesSmoke passaram. O Chromium com SwiftShader
validou `input-devices` nos dois modos, `camera-streaming` em worker e
`mobile-layout` em main. Com um controle simulado imóvel, a versão anterior
enviou 12 mensagens em 12 leituras do gamepad. O novo build enviou zero
durante 12 leituras em repouso e também durante 12 com o eixo mantido.

Próxima ação: publicar na main e repetir o cenário de entrada no site ao vivo.
