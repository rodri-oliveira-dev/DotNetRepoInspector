# Interface de linha de comando

**Idiomas:** [English](../en/cli.md) | Português (Brasil)

A CLI é a fronteira de entrega para executar o DotNetRepoInspector localmente ou por automação. Ela delega a análise do repositório ao `DotNetRepoInspector.Engine` e serializa o `InspectionReport` resultante usando o contrato JSON versionado do Core.

> A CLI está implementada, mas ainda não é empacotada como uma .NET Tool. O empacotamento da ferramenta e o comando público final `repo-inspect` são tratados separadamente. Durante o desenvolvimento, execute o projeto da CLI diretamente.

## Executar a partir do código-fonte

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- .
```

O primeiro argumento posicional é o caminho do repositório. Quando omitido, o diretório atual é inspecionado.

## Opções

```text
-o, --output <file>   Grava o JSON da inspeção em um arquivo em vez de stdout.
-v, --verbose         Emite logs operacionais detalhados em stderr.
    --debug           Emite logs operacionais de debug em stderr.
-h, --help            Exibe a ajuda.
    --version         Exibe a versão da CLI.
```

Apenas um caminho de repositório pode ser informado. A CLI é não interativa e não solicita valores ausentes, tornando seu comportamento adequado para CI.

## Streams de saída

Em uma inspeção normal sem `--output`, **stdout contém apenas o JSON da inspeção**. Logs operacionais, warnings e erros são gravados em **stderr**. Isso mantém seguros pipelines como:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . > inspection.json
```

Com `--output`, o JSON é gravado no arquivo UTF-8 solicitado e stdout permanece vazio:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . --output artifacts/inspection.json
```

O JSON é produzido por `InspectionJsonSerializer` e, portanto, segue o mesmo contrato versionado e determinístico documentado em [`schema/inspection-v1.md`](schema/inspection-v1.md).

## Códigos de saída

| Código | Significado |
| ---: | --- |
| `0` | A inspeção foi concluída e nenhum diagnóstico com severidade de erro foi produzido. |
| `1` | Um relatório foi produzido, mas contém um ou mais diagnósticos com severidade de erro. |
| `2` | Os argumentos da linha de comando são inválidos. |
| `3` | Uma falha fatal de inspeção ou serialização impediu a produção de um relatório utilizável. |
| `4` | O relatório não pôde ser gravado em stdout ou no arquivo solicitado. |
| `130` | A operação foi cancelada, incluindo interrupção do processo como Ctrl+C. |

O código `1` é intencionalmente diferente de uma falha fatal: o relatório JSON ainda existe e contém os diagnósticos estruturados que explicam o resultado parcial da inspeção.

## Cancelamento

O processo trata Ctrl+C de forma cooperativa. O cancellation token é propagado pela CLI até a engine de inspeção e seus adapters de longa duração. Uma execução cancelada termina com código `130` e não inicia um fluxo interativo de recuperação.

## Exemplos

Inspecionar o repositório atual e encaminhar o JSON para outro processo:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . | jq '.projects[].classification.kind'
```

Inspecionar outro repositório e salvar o relatório:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- ../service --output inspection.json
```

Habilitar detalhes operacionais sem contaminar o JSON em stdout:

```bash
dotnet run --project ./src/DotNetRepoInspector.Cli -- . --verbose > inspection.json
```
