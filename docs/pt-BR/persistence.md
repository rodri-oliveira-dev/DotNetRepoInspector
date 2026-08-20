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
                 InspectionSnapshotPublisher
                           |
                           v
                  IInspectionSnapshotSink
                           |
                           v
                 destino do consumidor
```

`DotNetRepoInspector.Persistence` depende somente de `DotNetRepoInspector.Core`. `Core` e `Engine` não dependem de persistência.

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

Nenhum sink é criado ou chamado pela engine de inspeção. Um host escolhe explicitamente um sink e chama `InspectionSnapshotPublisher` depois que a inspeção produz um `InspectionReport`.

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

## Retry e idempotência

O publisher genérico não faz retry. Uma política genérica não consegue saber se uma falha do destino é transitória ou se repetir a requisição é seguro.

Sinks concretos podem fazer retry somente quando todas as condições abaixo forem atendidas:

1. o adapter consegue classificar a falha como transitória;
2. quantidade de tentativas e backoff são limitados;
3. timeout total e cancelamento do chamador são respeitados;
4. replay utiliza a chave de idempotência do snapshot e é seguro para a semântica do destino.

O primeiro sink HTTP da issue #22 consumirá esse contrato em vez de inventar identidade específica do destino.

## Configuração e secrets

Configuração de persistência pertence à camada de delivery, não à configuração de inspeção do repositório. O `.dotnetrepoinspector.json` do repositório não deve conter credenciais de sinks.

Um futuro sink built-in poderá expor opções não sensíveis de seleção/política por inputs da CLI ou GitHub Action. Tokens, connection strings, API keys e valores equivalentes devem vir de variáveis de ambiente/secret stores apropriados ao host e nunca podem ser copiados para `InspectionReport`, `InspectionSnapshot`, contexto de diagnóstico ou logs normais.

Consulte [`security.md`](security.md) para as regras gerais de tratamento de secrets.

## Primeiro sink concreto

A ADR 0003 seleciona um adapter HTTP/webhook como primeiro sink built-in porque ele mantém o Inspector independente de bancos e cloud providers e funciona naturalmente em automações locais e CI/CD.

O adapter HTTP não é implementado aqui de propósito. A issue #22 é responsável por essa implementação e utilizará o contrato de identidade/idempotência definido pela ADR 0004.

## Decisões relacionadas

- [ADR 0003: Manter persistência opcional atrás de adapters de sink](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Definir proveniência e idempotência de snapshots a partir da evidência canônica](decisions/0004-snapshot-provenance-idempotency.md)
- [Proveniência e idempotência de snapshots](snapshot-provenance.md)
- [Schema de inspeção](schema/inspection-v1.md)
- [Segurança e privacidade](security.md)
