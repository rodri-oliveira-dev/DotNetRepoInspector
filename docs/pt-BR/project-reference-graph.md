# Grafo de referências entre projetos

**Idiomas:** [English](../en/project-reference-graph.md) | Português (Brasil)

O DotNetRepoInspector constrói arestas de referência entre projetos a partir da coleção **avaliada** de itens MSBuild `ProjectReference`, em vez de fazer parsing do XML bruto do projeto. Isso significa que condições, imports e transformações de itens do MSBuild são respeitados antes que uma dependência passe a fazer parte do grafo.

## Semântica dos caminhos

Os caminhos de referência no contrato normalizado são relativos à raiz da inspeção e usam `/` como separador.

- Um projeto dentro da raiz da inspeção é representado como `src/Library/Library.csproj`.
- Um projeto existente fora da raiz da inspeção é preservado usando um caminho relativo ao repositório, como `../Shared/Shared.csproj`.
- Caminhos absolutos do checkout não são expostos nos metadados normalizados de referência.

Isso mantém a saída estável entre diferentes máquinas sem deixar de tornar referências externas identificáveis.

## Referências não resolvidas

Um `ProjectReference` cujo destino avaliado não existe não é descartado. O grafo preserva a aresta e adiciona o diagnóstico de warning `DRI1003` (`ProjectReferenceUnresolved`) ao projeto de origem. O contexto do diagnóstico contém o `referencePath` normalizado e não expõe o caminho absoluto do checkout.

## Ciclos

O grafo é uma representação por adjacência. Referências circulares são representadas como arestas de saída comuns e não provocam travessia recursiva; portanto, ciclos como `A → B → A` são determinísticos e seguros para inspeção.
