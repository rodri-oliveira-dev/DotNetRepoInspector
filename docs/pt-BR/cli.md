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
-o, --output <file>          Grava o JSON da inspeção em um arquivo em vez de stdout.
    --config <file>          Usa um arquivo de configuração relativo ao repositório.
    --no-config              Ignora o arquivo padrão .dotnetrepoinspector.json.
    --exclude <path>         Exclui um diretório ou projeto relativo ao repositório. Repetível.
    --classify <path>=<kind> Sobrescreve a classificação de um projeto. Repetível.
    --sink http              Persiste um snapshot pelo sink HTTP/webhook built-in.
    --sink-url <url>         Endpoint HTTP/HTTPS usado pelo sink selecionado.
    --sink-timeout-seconds   Timeout total da persistência em segundos. Padrão: 15.
    --sink-failure-mode      Modo de falha da persistência: non-fatal ou fatal. Padrão: non-fatal.
    --sink-max-attempts      Máximo de tentativas HTTP para falhas transitórias. Padrão: 3.
-v, --verbose                Emite logs operacionais detalhados em stderr.
    --debug                  Emite logs operacionais de debug em stderr.
-h, --help                   Exibe a ajuda.
    --version                Exibe a versão da CLI/pacote.
```

Os tipos de classificação suportados são `web`, `worker`, `console`, `library`, `test` e `unknown`.

Apenas um caminho de repositório pode ser informado. A CLI é não interativa e não solicita valores ausentes, tornando seu comportamento adequado para CI.

## Contrato de descoberta de projetos

A descoberta de projetos trata extensões suportadas sem distinção entre maiúsculas e minúsculas. Arquivos terminados em `.csproj`, `.CSPROJ` ou qualquer combinação equivalente de caixa representam o mesmo formato de projeto suportado, enquanto a caixa original do caminho no filesystem é preservada no projeto reportado.

Automações e consumidores de inventário de frota devem tratar a coleção `projects` do relatório do Inspector como resultado canônico da descoberta. Não use uma pré-varredura case-sensitive separada, como `find ... -name '*.csproj'`, como fonte de verdade, pois ela pode divergir do Inspector em filesystems case-sensitive. Se uma pré-varredura for indispensável por otimização, ela deve usar comparação case-insensitive da extensão (por exemplo, `find ... -iname '*.csproj'`) e permanecer apenas consultiva, não autoritativa.

## Configuração do repositório

Quando `.dotnetrepoinspector.json` existe na raiz do repositório inspecionado, ele é carregado automaticamente. O arquivo pode definir exclusões relativas ao repositório e overrides explícitos de classificação. Ele é totalmente opcional; quando ausente, o comportamento zero-config existente é preservado.

Use `--config` para selecionar outro arquivo relativo ao repositório, ou `--no-config` para ignorar o carregamento automático do arquivo padrão. `--config` e `--no-config` são mutuamente exclusivos.

Valores diretos de `--exclude` são aditivos às exclusões do arquivo. Uma entrada direta de `--classify` tem precedência sobre o override do arquivo para o mesmo projeto. Consulte [`configuration.md`](configuration.md) para formato versionado, regras de caminho, proveniência dos overrides, diagnósticos e política completa de precedência.

Exemplos:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --exclude samples/Legacy.csproj \
  --classify src/App/App.csproj=web

dotnet repo-inspect . --config config/inspector.json
dotnet repo-inspect . --no-config --classify src/App/App.csproj=web
```

Valores malformados das opções da CLI são rejeitados antes da inspeção e retornam código `2`. Configuração inválida do repositório identificada pela Engine produz um relatório JSON normal com `DRI1013/error` e retorna código `1`.

## Persistência HTTP opcional de snapshots

A persistência permanece desabilitada enquanto `--sink http` não for selecionado. Uma execução normal continua inspecionando localmente sem contatar endpoint de persistência.

Exemplo mínimo:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

O adapter HTTP envia por POST o envelope canônico `InspectionSnapshot` depois que o relatório de inspeção já foi produzido. A chave de idempotência do snapshot é enviada no header `Idempotency-Key`, e apenas falhas classificadas como transitórias são repetidas.

O endpoint deve ser uma URL HTTP ou HTTPS absoluta e não pode conter credenciais embutidas. `--sink-timeout-seconds` aceita `1..300`; `--sink-max-attempts` aceita `1..5`.

Credenciais Bearer deliberadamente não são aceitas como argumentos de linha de comando. Forneça-as apenas por `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` ou secret facility equivalente do host:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Não coloque credenciais do sink em `.dotnetrepoinspector.json`, na URL do endpoint, em scripts versionados ou no JSON da inspeção.

`--sink-failure-mode non-fatal` é o padrão. Uma falha de persistência é registrada em stderr enquanto a semântica normal de códigos de saída da inspeção é preservada. Com `--sink-failure-mode fatal`, a falha de persistência retorna código `5` depois que o relatório já foi produzido.

Consulte [`persistence.md`](persistence.md) para proveniência do snapshot, idempotência, classificação de retry, timeout, cancelamento e semântica de segurança.

## Streams de saída

Em uma inspeção normal sem `--output`, **stdout contém apenas o JSON da inspeção**. Logs operacionais, warnings e erros são gravados em **stderr**. Isso mantém seguros pipelines como:

```bash
dotnet repo-inspect . > inspection.json
```

Com `--output`, o JSON é gravado no arquivo UTF-8 solicitado e stdout permanece vazio:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
```

Persistência não altera o contrato JSON de inspeção. O JSON é produzido por `InspectionJsonSerializer` e segue o mesmo contrato versionado e determinístico documentado em [`schema/inspection-v1.md`](schema/inspection-v1.md).

## Códigos de saída

| Código | Significado |
| ---: | --- |
| `0` | A inspeção foi concluída e nenhum diagnóstico com severidade de erro foi produzido. |
| `1` | Um relatório foi produzido, mas contém um ou mais diagnósticos com severidade de erro, incluindo configuração inválida do repositório. |
| `2` | Os argumentos da linha de comando são inválidos. |
| `3` | Uma falha fatal de inspeção ou serialização impediu a produção de um relatório utilizável. |
| `4` | O relatório não pôde ser gravado em stdout ou no arquivo solicitado. |
| `5` | A persistência do snapshot falhou enquanto configurada com `--sink-failure-mode fatal`. |
| `130` | A operação foi cancelada, incluindo inspeção, persistência ou interrupção do processo como Ctrl+C. |

O código `1` é intencionalmente diferente de uma falha fatal: o relatório JSON ainda existe e contém os diagnósticos estruturados que explicam o resultado parcial da inspeção. O código `5` também ocorre depois que o relatório de inspeção já foi produzido; ele representa falha no delivery do snapshot opcional, e não uma alteração nos diagnósticos da inspeção.

## Cancelamento

O processo trata Ctrl+C de forma cooperativa. O cancellation token é propagado pela CLI até a engine de inspeção e, quando habilitados, a publicação do snapshot e a requisição HTTP. Uma execução cancelada termina com código `130` e não inicia um fluxo interativo de recuperação.

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

Inspecionar, salvar o relatório local e exigir persistência do snapshot:

```bash
dotnet repo-inspect . \
  --output artifacts/inspection.json \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```
