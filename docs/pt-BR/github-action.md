# GitHub Action

**Idiomas:** [English](../en/github-action.md) | Português (Brasil)

O DotNetRepoInspector fornece uma GitHub Action reutilizável do tipo composite que executa a mesma .NET Tool e a mesma engine de inspeção usadas pela CLI. A Action é apenas um adapter de entrega: descoberta de projetos, avaliação MSBuild, classificação, configuração, diagnósticos, serialização JSON e semântica de códigos de saída continuam pertencendo ao Inspector existente.

> A implementação da Action já existe e é validada pelo CI do repositório, mas uma release pública `v1` ainda não foi publicada. `uses: rodri-oliveira-dev/DotNetRepoInspector@v1` passa a estar disponível depois que o fluxo de release publicar o pacote `DotNetRepoInspector` correspondente e criar as tags da Action.

## Uso mínimo

A Action não realiza checkout do código implicitamente:

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
| `config` | Não | vazio | Arquivo de configuração relativo ao repositório. Quando omitido, `.dotnetrepoinspector.json` é carregado automaticamente se existir. |
| `no-config` | Não | `false` | Use `true` para ignorar o arquivo padrão de configuração. Não pode ser combinado com `config`. |
| `exclude` | Não | vazio | Diretórios ou caminhos exatos de projeto, relativos ao repositório e separados por linhas, a serem excluídos. |
| `classify` | Não | vazio | Overrides explícitos `<project-path>=<kind>` separados por linhas. |

Os tipos de classificação suportados são `web`, `worker`, `console`, `library`, `test` e `unknown`.

Valores de `exclude` são aditivos às exclusões do arquivo. Uma entrada direta de `classify` vence o override do arquivo para o mesmo projeto. A Action encaminha esses valores para o mesmo contrato de configuração da CLI/Engine; ela não implementa lógica independente de classificação. Consulte [`configuration.md`](configuration.md).

A Action intencionalmente não expõe um input `inspector-version`. Cada revisão publicada da Action fixa uma versão exata da .NET Tool para que uma referência específica continue reproduzível.

## Configurar exclusões e overrides

```yaml
- name: Inspecionar repositório .NET
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    exclude: |
      generated
      samples/Legacy.csproj
    classify: |
      src/App/App.csproj=web
```

Um override altera somente a interpretação efetiva da classificação. Os fatos do MSBuild permanecem intactos. O schema `1.3` expõe `classification.source` e `classification.automaticKind` quando um override está ativo, permitindo que automações distingam o resultado configurado do automático.

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
- name: Consumir metadados da inspeção
  shell: pwsh
  env:
    REPORT_PATH: ${{ steps.inspect.outputs.report-path }}
    SCHEMA_VERSION: ${{ steps.inspect.outputs.schema-version }}
  run: |
    Write-Host "Schema: $env:SCHEMA_VERSION"
    $report = Get-Content -LiteralPath $env:REPORT_PATH -Raw | ConvertFrom-Json
    Write-Host "Projetos: $(@($report.projects).Count)"
```

## Comportamento dos códigos de saída

A Action preserva os códigos de saída da CLI:

| Código | Significado |
| ---: | --- |
| `0` | Inspeção concluída sem diagnósticos de erro. |
| `1` | Um relatório foi produzido, mas contém um ou mais diagnósticos `error`, incluindo configuração inválida do repositório (`DRI1013`). |
| `2` | Argumentos inválidos chegaram à fronteira da CLI/Action. |
| `3` | Uma falha fatal de inspeção impediu um relatório normal. |
| `4` | Não foi possível gravar o relatório. |
| `130` | A inspeção foi cancelada. |

Quando a CLI retorna um código diferente de zero, a Action publica os outputs ainda disponíveis e termina com o mesmo código. Um workflow que queira examinar esses outputs após uma falha pode usar `continue-on-error` e uma etapa posterior com `if: always()`.

## Runtime e SDKs do repositório

A Composite Action instala o SDK .NET 10 necessário para executar o Inspector usando `actions/setup-dotnet` fixado por SHA completo dentro do `action.yml`.

Esse bootstrap é separado dos SDKs exigidos pelo repositório inspecionado. Se o `global.json` exigir um SDK ausente no runner, o workflow deve instalá-lo antes de executar o Inspector.

## Bootstrap da Tool e isolamento de package source

A Action instala `DotNetRepoInspector` em um diretório específico da invocação sob `RUNNER_TEMP` usando `dotnet tool install --tool-path`.

Ela não instala a Tool globalmente, não modifica o tool manifest do repositório, não confia em fontes de `NuGet.config` do repositório/máquina para resolver o Inspector e não seleciona uma versão flutuante ou fornecida pelo consumidor. Em vez disso, cria uma configuração NuGet temporária com fontes herdadas removidas e resolve no NuGet.org a versão exata fixada.

O CI do próprio repositório possui um hook de self-test estritamente limitado que pode substituir apenas o package source pelo pacote `1.0.0` gerado localmente. Esse hook é rejeitado fora de `rodri-oliveira-dev/DotNetRepoInspector` e não altera a versão fixada nem os inputs públicos.

## Permissões e fronteira de confiança

Inspecionar um repositório já em checkout não requer acesso à API do GitHub, token, segredo ou permissão de escrita. O consumidor continua responsável pelas permissões usadas no próprio checkout e nas etapas posteriores.

A avaliação MSBuild não é um sandbox. O Inspector avalia configurações controladas pelo repositório conforme a [ADR 0001](decisions/0001-msbuild-evaluation-strategy.md), portanto código não confiável não deve ser inspecionado em workflow privilegiado com segredos sem revisão explícita de confiança.

## Versionamento

A Action segue a [ADR 0002](decisions/0002-github-action-distribution-strategy.md): tags imutáveis fixam a mesma versão exata do pacote Inspector, aliases móveis de major/minor só podem avançar dentro dos limites de compatibilidade e a Action `v1` permanece dentro da major `1` do schema de inspeção.

## Validação no CI

O CI do repositório executa a própria Composite Action com `uses: ./` em Ubuntu, Windows e macOS. O smoke test gera localmente a versão exata usada pela Action, instala pelo mesmo bootstrap isolado, executa uma inspeção real, valida os outputs e exercita os inputs `exclude` e `classify`. Um cenário adicional no Ubuntu confirma a propagação de um resultado não zero e do relatório parcial correspondente.
