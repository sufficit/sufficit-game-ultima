# Corrigir a renderização do terreno

Restaurar o chão visível, com a trilha e a paleta procedural alinhadas às coordenadas do mundo. Validar os modos padrão e main sem gravar no mundo de produção.

1. [concluído] Reproduzir em produção e identificar a causa.
2. [concluído] Corrigir a renderização e verificar o caminho de edição de superfície em ambiente isolado.
3. [concluído] Executar testes relevantes e `dotnet publish src/Ultima.csproj -c Release`.
4. [em andamento] Enviar à main, aguardar o GitHub Pages, validar a versão publicada e converter o plano em atividade.

Reprodução: produção 5a7fea4 em WebGL2 mostra o céu sob a vegetação com os chunks carregados. As linhas avançam em +Z, ao contrário do GroundBuilder. ComputeNormals do Babylon 9.36.0 confirma normal -Y para os índices atuais.

Validação isolada no Chromium com WebGL2: chão com trilha e vegetação, textura de 128 px e atualização de pintura de [158, 128, 87] para [244, 249, 254], além de alteração de altura de 0,05 m para 4,05 m no centro.

Checagens locais: dotnet publish Release, RulesSmoke, quatro testes Node e git diff --check passaram. O publish emitiu os avisos existentes CS8601 e CS8625 em Index.razor.

Próxima ação: publicar a correção e conferir o build servido pelo Pages.
