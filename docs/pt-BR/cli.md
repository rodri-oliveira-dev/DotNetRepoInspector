# Interface de linha de comando

**Idiomas:** [English](../en/cli.md) | Português (Brasil)

A CLI é a fronteira de entrega para executar o DotNetRepoInspector localmente ou por automação. Ela delega a análise do repositório ao `DotNetRepoInspector.Engine` e serializa o `InspectionReport` resultante usando o contrato JSON versionado do Core.

O DotNetRepoInspector é empacotado como uma .NET Tool com package ID `DotNetRepoInspector` e comando da tool `dotnet-repo-inspect`. Como o comando usa o prefixo `dotnet-`, a invocação pública suportada é:

```bash
dotnet repo-inspect .
```

O comando direto `dotnet-repo-inspect .` também é válido para uma tool instalada globalmente.

> O repositório está configurado e validado em CI para empacotamento como .NET Tool, mas o pacote ainda não foi publicado no NuGet.org. Até existir uma release publicada, use o fluxo de pacote local abaixo ao validar a distribuição.

## Instalar como .NET Tool

A tool atual tem como alvo .NET 10 e, portanto, requer um runtime/SDK .NET compatível na máquina onde será executada.

### Instalação global

Depois que o pacote for publicado em um feed NuGet:

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --help
```

Atualize uma instalação global com:

```bash
dotnet tool update --global DotNetRepoInspector
```

Remova a tool com:

```bash
dotnet tool uninstall --global DotNetRepoInspector
```

### Instalação local

Um repositório pode fixar a tool em um tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

Quando já existir um manifest, não o recrie. Atualize a tool local fixada a partir de um diretório coberto pelo manifest:

```bash
dotnet tool update DotNetRepoInspector
```

Restaure as tools declaradas em um manifest existente com:

```bash
dotnet tool restore
```

### Gerar e instalar a partir deste repositório

A versão do pacote pode ser fornecida no momento do pack sem editar o arquivo de projeto:

```bash
dotnet pack ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
  --configuration Release \
  --output ./artifacts/packages \
  -p:Version=0.0.0-local
```

Instale globalmente esse pacote exato usando o feed local:

```bash
dotnet tool install --global DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --version
```

Para um manifest local, execute a partir do diretório do manifest:

```bash
dotnet tool install DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --help
```

O CI usa uma fonte NuGet local isolada e valida os metadados e o conteúdo do pacote, instalação global, instalação local, `--help`, `--version` e uma inspeção real de fixture antes que o job obrigatório `Build, test and quality` possa passar.

## Executar a partir do código-fonte

Contribuidores ainda podem executar o projeto da CLI diretamente sem empacotá-lo:

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
    --version         Exibe a versão da CLI/pacote.
```

Apenas um caminho de repositório pode ser informado. A CLI é não interativa e não solicita valores ausentes, tornando seu comportamento adequado para CI.

## Streams de saída

Em uma inspeção normal sem `--output`, **stdout contém apenas o JSON da inspeção**. Logs operacionais, warnings e erros são gravados em **stderr**. Isso mantém seguros pipelines como:

```bash
dotnet repo-inspect . > inspection.json
```

Com `--output`, o JSON é gravado no arquivo UTF-8 solicitado e stdout permanece vazio:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
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
dotnet repo-inspect . | jq '.projects[].classification.kind'
```

Inspecionar outro repositório e salvar o relatório:

```bash
dotnet repo-inspect ../service --output inspection.json
```

Habilitar detalhes operacionais sem contaminar o JSON em stdout:

```bash
dotnet repo-inspect . --verbose > inspection.json
```
