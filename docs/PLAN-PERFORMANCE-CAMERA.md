# Custo da colisão da câmera

1. [concluído] Medir a colisão da câmera em uma sequência determinística
   de órbitas e identificar o trecho mais caro.
2. [concluído] Migrar o trecho adequado para C# ou reduzir o trabalho por quadro,
   preservando o alvo estabilizado, a precisão da colisão e o streaming.
3. [em andamento] Repetir a medição, validar worker e main em Release e testar a
   revisão publicada no GitHub Pages.

O orçamento é 16,67 ms por quadro para 60 FPS. O perfil mede a câmera
isoladamente e o custo de quadro separadamente, em Chromium com SwiftShader.

Linha de base em produção: 120 obstáculos e 9.000 raios variados. A
triagem e colisão de malhas somou 39,1 ms. As amostras de terreno
somaram 29,7 ms. Mediana do lote de 1.800 raios: 13,1 ms.

Comparação com o cenário C# `camera-performance`: as 1.800 distâncias
coincidiram. As candidatas caíram de 120 para 23 e a mediana do lote
caiu de 9,9 ms para 6,6 ms. As mudanças de lista consultam o WASM por
setor, aumento de alcance ou mudança de geometria.
