# Persistência opcional de snapshots

**Idiomas:** [English](../en/persistence.md) | Português (Brasil)

O DotNetRepoInspector trata persistência como uma integração opcional posterior à inspeção. Um repositório sempre pode ser inspecionado sem banco de dados, endpoint HTTP, conta de cloud ou serviço específico de CI.

O `InspectionReport` normalizado continua pertencendo ao `DotNetRepoInspector.Core`. Persistência não adiciona falhas de transporte, credenciais, estado de retry ou campos específicos de destino a esse relatório.

## Fronteira

O fluxo é deliberadamente sequencial:

```text
Repositório
    |
    v
Inspection Engine ----> InspectionReport
                           |
                           | opcional
                           v
                 InspectionSnapshotFactory
                           |
                           v
                 InspectionSnapshotPublisher
                           |
                           v
                  IInspectionSnapshotSink
                           |
                           v
                 destino do consumidor
```

`DotNetRepoInspector.Persistence` depende somente de `DotNetRepoInspector.Core`. O adapter HTTP built-in vive no assembly separado `DotNetRepoInspector.Persistence.Http`. `Core` e `Engine` não dependem de persistência nem de HTTP.

## Contrato de extensão

Destinos de terceiros implementam `IInspectionSnapshotSink`:

```csharp
public interface IInspectionSnapshotSink
{
    string Name { get; }

    Task<InspectionSinkWriteResult> WriteAsync(
        InspectionSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
```

Um sink deve:

- usar um `Name` estável e sem secrets;
- respeitar cancelamento;
- retornar falhas operacionais por `InspectionSinkWriteResult.Failed(...)`;
- classificar uma falha como transitória somente quando o adapter puder fazê-lo de forma confiável;
- nunca colocar credenciais, headers de autorização, connection strings, dumps brutos de exceção ou corpos de resposta contendo secrets nas mensagens de falha;
- não modificar o relatório de inspeção.

Exceções inesperadas que atravessem a fronteira de extensão são convertidas por `InspectionSnapshotPublisher` no resultado genérico `unexpected-sink-failure`, sem expor o texto da exceção.

## Comportamento opt-in

Nenhum sink é criado ou chamado pela engine de inspeção. A CLI ou outro host de delivery escolhe explicitamente um sink e chama `InspectionSnapshotPublisher` depois que a inspeção produz um `InspectionReport`.

Sem `--sink`, a CLI mantém o comportamento existente de não fazer acesso de rede para persistência. A configuração do repositório em `.dotnetrepoinspector.json` não habilita persistência.

As opções genéricas de persistência são independentes das credenciais específicas do sink:

- `Timeout`: 15 segundos por padrão;
- `FailureMode`: `NonFatal` por padrão, ou `Fatal` quando persistência for obrigatória para a pipeline.

`InspectionPersistenceResult.ShouldFailExecution` informa à camada de delivery se uma falha de persistência deve fazer o comando/job falhar. O relatório de inspeção permanece inalterado nos dois modos.

Falhas de persistência não são diagnósticos `DRI`, porque descrevem o envio de um relatório já produzido, e não a inspeção do repositório.

## Proveniência e idempotência do snapshot

Antes da publicação, o host cria um `InspectionSnapshot` por meio de `InspectionSnapshotFactory`. O envelope contém versão do Inspector, identidade canônica do repositório, commit/ref, instante UTC da observação, metadados genéricos opcionais da execução, digest normalizado do relatório e chave de idempotência versionada.

Dois escopos ficam explícitos:

- `RepositoryState` para commits limpos com identidade canônica de remote, permitindo que reexecuções equivalentes compartilhem a chave;
- `Observation` para estados sujos ou ambíguos, evitando deduplicação acidental.

Consulte [`snapshot-provenance.md`](snapshot-provenance.md) e a ADR 0004 para o contrato completo.

## Sink HTTP/webhook built-in

A ADR 0003 seleciona entrega HTTP/webhook como primeiro sink built-in. `HttpInspectionSnapshotSink` envia o envelope canônico de snapshot para um endpoint fornecido pelo consumidor com:

- HTTP `POST`;
- `Content-Type: application/json`;
- payload produzido por `InspectionSnapshotJsonSerializer`;
- `Idempotency-Key: <snapshot.idempotencyKey>`;
- `Authorization: Bearer <token>` opcional quando o token é fornecido pelo ambiente do host.

O endpoint deve ser uma URL HTTP ou HTTPS absoluta. URLs contendo user information embutida, como `https://user:password@example/...`, são rejeitadas. O adapter nunca lê o corpo da resposta para compor uma mensagem de falha.

### Configuração pela CLI

Habilite o sink explicitamente:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Política de delivery opcional:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-timeout-seconds 30 \
  --sink-max-attempts 3 \
  --sink-failure-mode fatal
```

As opções suportadas para o sink HTTP são:

| Opção | Padrão | Significado |
| --- | --- | --- |
| `--sink http` | desabilitado | Seleciona explicitamente o sink HTTP built-in. |
| `--sink-url <url>` | nenhum | Endpoint HTTP/HTTPS absoluto fornecido pelo consumidor. Obrigatório quando o sink está habilitado. |
| `--sink-timeout-seconds <1..300>` | `15` | Deadline total da persistência, incluindo retries. |
| `--sink-max-attempts <1..5>` | `3` | Quantidade máxima de tentativas HTTP. |
| `--sink-failure-mode non-fatal|fatal` | `non-fatal` | Define se uma falha de persistência deve falhar o comando/pipeline. |

Intencionalmente **não existe argumento de CLI para token**. Para autenticação Bearer, defina `DOTNET_REPO_INSPECTOR_HTTP_TOKEN` no ambiente do processo ou secret facility equivalente:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . --sink http --sink-url https://evidence.example/api/snapshots
```

Não coloque tokens em `--sink-url`, argumentos de linha de comando, `.dotnetrepoinspector.json`, scripts versionados ou no JSON de inspeção.

## Retry e classificação de falhas

O publisher genérico não faz retry. Retry pertence ao adapter HTTP porque ele consegue classificar falhas de transporte/HTTP e repetir de forma segura o mesmo snapshot usando sua chave de idempotência.

O sink HTTP faz retry somente para:

- falhas de transporte `HttpRequestException`;
- timeouts de requisição não causados por cancelamento do chamador;
- HTTP `408`, `429`, `500`, `502`, `503` e `504`.

Os retries usam backoff exponencial limitado, respeitam a quantidade máxima de tentativas configurada e permanecem dentro da fronteira total de timeout/cancelamento do publisher.

Falhas de autenticação (`401`/`403`), `404` e outras respostas `4xx` não transitórias não são repetidas. As mensagens usam classificações estáveis e não copiam texto de exceção nem corpo da resposta.

## Delivery fatal e non-fatal

O relatório é produzido antes da tentativa de persistência.

No modo padrão `non-fatal`, uma falha de persistência é registrada em stderr, mas a semântica normal de códigos de saída da inspeção é preservada. Com `fatal`, uma falha de persistência retorna o código de saída `5` da CLI. O `InspectionReport` já produzido não recebe diagnóstico de persistência em nenhum dos modos.

Cancelamento do chamador, incluindo Ctrl+C, é propagado pela publicação do snapshot e pela requisição HTTP e termina pelo fluxo normal de cancelamento (`130`).

## GitHub Action

A Action reutilizável expõe `sink-url`, `sink-token`, `sink-timeout-seconds`, `sink-failure-mode` e `sink-max-attempts`. `sink-token` deve sempre referenciar um secret do GitHub Actions. A Action o mapeia diretamente para `DOTNET_REPO_INSPECTOR_HTTP_TOKEN`; o valor não é adicionado à lista de argumentos da CLI.

Exemplo:

```yaml
- name: Inspecionar e persistir evidência
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    sink-url: https://evidence.example/api/snapshots
    sink-token: ${{ secrets.INSPECTOR_EVIDENCE_TOKEN }}
    sink-failure-mode: fatal
```

Quando a persistência está habilitada, a Action também fornece proveniência genérica da execução (`run_id:run_attempt`, provider e ref) para a snapshot factory, sem adicionar tipos específicos do GitHub ao contrato de persistência.

## Configuração e secrets

Configuração de persistência pertence à camada de delivery, não à configuração de inspeção do repositório. O `.dotnetrepoinspector.json` do repositório não deve conter credenciais de sinks.

Tokens, connection strings, API keys e valores equivalentes devem vir de variáveis de ambiente/secret stores apropriados ao host e nunca podem ser copiados para `InspectionReport`, `InspectionSnapshot`, contexto de diagnóstico ou logs normais.

Consulte [`security.md`](security.md) para as regras gerais de tratamento de secrets.

## Decisões relacionadas

- [ADR 0003: Manter persistência opcional atrás de adapters de sink](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Definir proveniência e idempotência de snapshots a partir da evidência canônica](decisions/0004-snapshot-provenance-idempotency.md)
- [Proveniência e idempotência de snapshots](snapshot-provenance.md)
- [CLI](cli.md)
- [GitHub Action](github-action.md)
- [Schema de inspeção](schema/inspection-v1.md)
- [Segurança e privacidade](security.md)
