# GitHub Action

**Idiomas:** [English](../en/github-action.md) | Português (Brasil)

O DotNetRepoInspector fornece uma GitHub Action reutilizável do tipo composite que executa a mesma .NET Tool e a mesma engine de inspeção usadas pela CLI. A Action é apenas um adapter de entrega: descoberta de projetos, avaliação MSBuild, classificação, diagnósticos, serialização JSON e semântica de códigos de saída continuam pertencendo ao Inspector existente.

> A implementação da Action já existe e é validada pelo CI do repositório, mas uma release pública `v1` ainda não foi publicada. `uses: rodri-oliveira-dev/DotNetRepoInspector@v1` passa a estar disponível depois que o fluxo de release publicar o pacote `DotNetRepoInspector` correspondente e criar as tags da Action.

## Uso mínimo

A Action não realiza checkout do código implicitamente. O workflow deve fazer checkout do repositório antes:

```yaml
steps:
  - name: Checkout
    uses: actions/checkout@v7

  - name: Inspecionar repositório .NET
    id: inspect
    uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
```

A própria Action não precisa de token do GitHub nem de permissão de escrita para inspecionar um repositório que já esteja em checkout.

## Inputs

| Input | Obrigatório | Padrão | Descrição |
| --- | --- | --- | --- |
| `path` | Não | `.` | Diretório do repositório a inspecionar. Caminhos relativos são resolvidos a partir de `GITHUB_WORKSPACE`. |
| `output` | Não | Arquivo temporário pertencente à Action | Destino do relatório JSON. Caminhos relativos são resolvidos a partir de `GITHUB_WORKSPACE`. Diretórios-pai são criados quando necessário. |
| `verbosity` | Não | `normal` | Nível de logging operacional: `normal`, `verbose` ou `debug`. |

A Action intencionalmente não expõe um input `inspector-version`. Cada revisão publicada da Action fixa uma versão exata da .NET Tool para que uma referência específica da Action continue reproduzível.

## Outputs

| Output | Descrição |
| --- | --- |
| `report-path` | Caminho absoluto para o JSON de inspeção gerado quando existe relatório. |
| `schema-version` | `schemaVersion` lido do relatório gerado quando disponível. |
| `inspector-version` | Versão exata da .NET Tool `DotNetRepoInspector` fixada por esta revisão da Action. |
| `exit-code` | Código de saída retornado pela CLI. |

O relatório JSON completo permanece intencionalmente em arquivo, em vez de ser copiado para `$GITHUB_OUTPUT`.

## Salvar e consumir o relatório

```yaml
steps:
  - name: Checkout
    uses: actions/checkout@v7

  - name: Inspecionar repositório .NET
    id: inspect
    uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
      output: artifacts/inspection.json
      verbosity: verbose

  - name: Consumir metadados da inspeção
    shell: pwsh
    env:
      REPORT_PATH: ${{ steps.inspect.outputs.report-path }}
      SCHEMA_VERSION: ${{ steps.inspect.outputs.schema-version }}
      INSPECTOR_VERSION: ${{ steps.inspect.outputs.inspector-version }}
    run: |
      Write-Host "Schema: $env:SCHEMA_VERSION"
      Write-Host "Inspector: $env:INSPECTOR_VERSION"
      $report = Get-Content -LiteralPath $env:REPORT_PATH -Raw | ConvertFrom-Json
      Write-Host "Projetos: $(@($report.projects).Count)"
```

## Comportamento dos códigos de saída

A Action preserva os códigos de saída da CLI em vez de traduzi-los para uma política própria:

| Código | Significado |
| ---: | --- |
| `0` | Inspeção concluída sem diagnósticos de erro. |
| `1` | Um relatório foi produzido, mas contém um ou mais diagnósticos `error`. |
| `2` | Argumentos inválidos chegaram à fronteira da CLI/Action. |
| `3` | Uma falha fatal de inspeção impediu um relatório normal. |
| `4` | Não foi possível gravar o relatório. |
| `130` | A inspeção foi cancelada. |

Quando a CLI retorna um código diferente de zero, a Action publica os outputs que ainda estiverem disponíveis e termina com o mesmo código. Um workflow que queira inspecionar deliberadamente esses outputs após uma falha pode usar os mecanismos normais do GitHub Actions, como `continue-on-error` e uma etapa posterior com `if: always()`.

## Runtime e SDKs do repositório

A Composite Action instala o SDK .NET 10 necessário para executar o Inspector usando `actions/setup-dotnet` fixado por SHA completo dentro do `action.yml`.

Esse bootstrap é separado dos SDKs exigidos pelo repositório inspecionado. Se o `global.json` do repositório exigir um SDK que não esteja disponível no runner, o workflow deve instalar esse SDK antes de executar o Inspector.

Por exemplo, um repositório que exige .NET 8 e é inspecionado pelo Inspector baseado em .NET 10 pode instalar as duas famílias de SDK lado a lado.

## Bootstrap da Tool e isolamento de package source

A Action instala `DotNetRepoInspector` em um diretório específico da invocação sob `RUNNER_TEMP`, usando `dotnet tool install --tool-path`.

Ela não:

- instala a Tool globalmente;
- modifica o tool manifest do repositório inspecionado;
- confia nos package sources de `NuGet.config` do repositório ou da máquina para resolver o pacote do Inspector;
- seleciona `latest`, wildcard ou uma versão arbitrária fornecida pelo consumidor.

Em vez disso, a Action cria uma configuração NuGet temporária com os package sources herdados removidos e resolve no NuGet.org apenas a versão exata fixada. Esse isolamento vale somente para o bootstrap do Inspector e não altera a configuração de pacotes usada pelo repositório inspecionado.

O CI do próprio repositório possui um hook de self-test estritamente limitado que pode substituir apenas o package source pelo pacote `1.0.0` gerado localmente. Esse hook é rejeitado fora de `rodri-oliveira-dev/DotNetRepoInspector` e não altera a versão fixada nem os inputs públicos da Action.

## Permissões e fronteira de confiança

Inspecionar um repositório já em checkout não requer acesso à API do GitHub, token, segredo ou permissão de escrita. O consumidor continua responsável pelas permissões usadas no próprio checkout e nas etapas posteriores do workflow.

A avaliação MSBuild não é um sandbox. O Inspector avalia configurações MSBuild controladas pelo repositório de acordo com a [ADR 0001](decisions/0001-msbuild-evaluation-strategy.md), portanto repositórios não confiáveis não devem ser inspecionados em workflows privilegiados que possuam segredos sem uma revisão explícita de confiança.

## Versionamento

A Action segue a [ADR 0002](decisions/0002-github-action-distribution-strategy.md):

- uma tag imutável como `v1.2.3` fixa exatamente o pacote `DotNetRepoInspector` `1.2.3`;
- aliases móveis como `v1` e, opcionalmente, `v1.2` só podem apontar para releases compatíveis;
- a major `v1` da Action deve permanecer compatível com a major `1` do schema de inspeção;
- publicação do pacote e movimentação das tags da Action são responsabilidades do fluxo de release e não ocorrem na validação de pull requests.

## Validação no CI

O CI do repositório executa a própria Composite Action com `uses: ./` em Ubuntu, Windows e macOS. O smoke test gera localmente a versão exata usada pela Action, instala o pacote pelo mesmo caminho de bootstrap isolado, executa uma inspeção real de fixture e valida `report-path`, `schema-version`, `inspector-version` e `exit-code`.

Um cenário adicional no Ubuntu inspeciona a fixture de SDK ausente e confirma que o código de saída `1`, o relatório JSON parcial e o diagnóstico `DRI1002` são preservados pelo wrapper da Action.
