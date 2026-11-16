# Motor de regras — D&D 2024 (SRD 5.2.1)

O projeto usa como referência de regras o **System Reference Document 5.2.1**, publicado para a revisão de 2024 de Dungeons & Dragons (também chamada informalmente de 5.5e).

## Atribuição e licença

Este trabalho inclui material do **System Reference Document 5.2.1 (“SRD 5.2.1”)**, de Wizards of the Coast LLC, disponível em <https://www.dndbeyond.com/srd>. O SRD 5.2.1 está licenciado sob a **Creative Commons Attribution 4.0 International License (CC BY 4.0)**: <https://creativecommons.org/licenses/by/4.0/>.

Documento oficial consultado: [SRD 5.2.1 em inglês (PDF)](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf).

## Implementado nesta primeira fatia

- RNG pseudoaleatório com semente reproduzível (xoshiro256**/SplitMix64), amostragem uniforme por rejeição e fonte de semente do sistema.
- Notação comum de dados, `kh`/`kl`, modificadores e duplicação de dados em acerto crítico sem duplicar bônus fixos.
- Testes de d20, vantagem/desvantagem, ataque, crítico, iniciativa e regras de modificadores.
- Seis atributos, array padrão, compra de pontos (27) e geração aleatória `4d6kh3`.
- Bônus de proficiência e limites de XP/nível 1–20.
- Dados de vida, proficiências de salvaguarda, PV, CA sem armadura e iniciativa das 12 classes do SRD.
- Testes de morte, dano a 0 PV, estabilização, morte por dano massivo e recuperação de personagem Estável.

## Escopo ainda não implementado

Este módulo é a fundação, não a implementação integral do sistema: ainda faltam armas e propriedades, armaduras/equipamentos, características e subclasses de classe, antecedente/espécie, perícias, magias, descanso e recuperação, condições, ações de combate, multiclasse e integração da ficha com a interface do jogo.

## Validação local

Execute `dotnet run --project tests/RulesSmoke -c Release` na raiz do workspace. O harness não depende de pacotes externos e testa regras/aleatoriedade sem renderizador ou servidor.
