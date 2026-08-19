# Desempenho e escalabilidade

**Idiomas:** [English](../en/performance.md) | Português (Brasil)

O DotNetRepoInspector usa um repositório sintético reproduzível para medir o custo da inspeção antes de introduzir otimizações de desempenho. O objetivo é detectar regressões relevantes mantendo correção, semântica de resolução de SDK e saída determinística acima de micro-otimizações.

## Cenário de referência

O harness de desempenho fica em `benchmarks/DotNetRepoInspector.Performance` e cria o repositório em um diretório temporário antes do início da região cronometrada.

O cenário de referência versionado é `synthetic-100-projects`:

- 100 projetos SDK-style sob `src/`;
- `Directory.Build.props` fornece `net8.0`, nullable e implicit usings;
- um `global.json` no repositório solicita o SDK .NET `10.0.100` com roll-forward `latestFeature`;
- cada projeto após o primeiro referencia o projeto anterior, formando uma cadeia de dependências com 100 projetos;
- os projetos não contêm código de aplicação, mantendo a medição focada em inspeção do repositório e avaliação MSBuild, e não em compilação.

O harness aceita outras quantidades por `--project-count`, mas a baseline de regressão do CI fica deliberadamente vinculada a 100 projetos para que as comparações usem a mesma carga.

## O que é medido

Uma inspeção fria registra:

- tempo de descoberta de projetos;
- tempo agregado de `IMsBuildProjectFactsEvaluator`;
- tempo de serialização JSON;
- overhead restante da inspeção, incluindo metadados de repositório/SDK e normalização;
- tempo da inspeção e end-to-end;
- bytes managed alocados pelo processo do Inspector durante a região medida;
- peak working set do processo do Inspector;
- quantidade de projetos descobertos e avaliações de projeto;
- tamanho do JSON serializado.

A criação do repositório sintético, o restore e o build do harness ficam fora da região medida.

As medições de memória são aproximadas. Alocação managed e peak working set descrevem o processo principal do Inspector. Elas **não** agregam a memória dos processos filhos `dotnet`/MSBuild, portanto servem para detectar regressões no próprio Inspector, e não como estimativa da memória total da máquina necessária para uma inspeção.

## Baseline observada em 2026-08-19

A baseline foi estabelecida em runners Ubuntu 24.04 hospedados pelo GitHub com SDK .NET `10.0.400`. Foram executadas duas medições equivalentes antes de definir um limite.

| Métrica | Execução 1 | Execução 2 |
| --- | ---: | ---: |
| Projetos descobertos / avaliados | 100 / 100 | 100 / 100 |
| Descoberta | 9,87 ms | 8,39 ms |
| Avaliação MSBuild | 49.442,25 ms | 56.995,26 ms |
| Serialização | 35,06 ms | 41,33 ms |
| Outro overhead de inspeção | 210,28 ms | 204,34 ms |
| End-to-end | 49.697,46 ms | 57.249,32 ms |
| Alocações managed | 18,16 MiB | 18,17 MiB |
| Peak working set | 79,36 MiB | 79,14 MiB |

A execução mais lenta é registrada em `.github/performance-baseline.json` como referência observada conservadora. O tempo end-to-end variou aproximadamente 15% entre os dois runners hospedados, enquanto alocação managed e working set permaneceram praticamente estáveis.

## Evidência de hotspot

A avaliação dos fatos MSBuild representa mais de 99,5% do tempo de inspeção medido nas duas execuções. Descoberta e serialização ficam na ordem de dezenas de milissegundos e não são alvos atuais de otimização.

O harness também verifica que ocorrem exatamente 100 avaliações de fatos para 100 projetos descobertos. Portanto, não há invocação duplicada de `IMsBuildProjectFactsEvaluator` pela engine nesse cenário.

Cada avaliação de projeto continua seguindo o ADR 0001: um preflight de resolução do SDK (`dotnet --version`) seguido da consulta avaliada via `dotnet msbuild`. Startup de processos filhos e avaliação MSBuild são, consequentemente, o principal custo de escalabilidade. Esse comportamento é deliberado porque a seleção do SDK depende do diretório do projeto e do `global.json` aplicável.

## Decisões sobre cache e paralelismo

Este trabalho de performance não introduz cache nem avaliação paralela de projetos.

Um cache de resolução de SDK por raiz do repositório seria incorreto quando `global.json` aninhados selecionam SDKs diferentes. Um cache seguro precisaria ser limitado a uma única inspeção e indexado pelo contexto efetivo de resolução do SDK, com fixtures comprovando o comportamento de configurações aninhadas antes de medir o ganho.

Da mesma forma, paralelismo sem limite poderia criar muitos processos `dotnet`/MSBuild simultâneos, aumentando pressão de CPU e memória e tornando o CI menos previsível. Ainda não existe benchmark comparativo demonstrando um ganho líquido seguro, então a engine permanece sequencial. Uma otimização futura deve usar concorrência limitada somente depois de testes de correção e medições antes/depois que a justifiquem.

## Guardrail de regressão

O workflow `Validate performance` executa no Ubuntu quando código relevante à performance, o harness, seu workflow ou a baseline são alterados. Também pode ser iniciado manualmente por `workflow_dispatch`.

Os limites versionados são deliberadamente mais amplos do que a variação observada nos runners hospedados:

| Guardrail | Limite |
| --- | ---: |
| Avaliação MSBuild agregada | 85.000 ms |
| End-to-end | 90.000 ms |
| Alocações managed | 32 MiB |
| Peak working set do Inspector | 128 MiB |

Esses valores são **limites de regressão, não promessas de desempenho**. Eles buscam detectar grandes degradações acidentais, avaliações duplicadas ou crescimento significativo de memória do Inspector sem tornar o CI instável por ruído normal dos runners hospedados. Qualquer aumento da baseline deve ser revisado com novas medições; o workflow nunca a atualiza automaticamente.

O job de performance possui timeout de 10 minutos no GitHub Actions, enquanto o próprio benchmark cancela após 300 segundos. A CLI normal já propaga cancelamento, incluindo Ctrl+C, para a inspeção e para os processos filhos. Não é imposto um timeout universal de wall-clock na CLI porque tamanhos de repositório e cargas MSBuild válidas variam muito; cada consumidor pode definir um timeout de CI adequado ao seu ambiente.

## Executando localmente

Faça o build uma vez e depois execute o cenário fixo da baseline:

```bash
dotnet restore ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj
dotnet build ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj --configuration Release --no-restore

dotnet run \
  --project ./benchmarks/DotNetRepoInspector.Performance/DotNetRepoInspector.Performance.csproj \
  --configuration Release \
  --no-build \
  -- \
  --project-count 100 \
  --timeout-seconds 300 \
  --output ./artifacts/performance/metrics.json \
  --summary ./artifacts/performance/summary.md \
  --baseline ./.github/performance-baseline.json
```

Execute sem `--baseline` ao coletar medições exploratórias para outra quantidade de projetos. Essas medições são úteis para investigação, mas não são diretamente comparáveis à baseline versionada de 100 projetos usada no CI.
