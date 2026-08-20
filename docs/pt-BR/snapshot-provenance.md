# Proveniência e idempotência de snapshots

**Idiomas:** [English](../en/snapshot-provenance.md) | Português (Brasil)

Evidências persistidas são representadas por `InspectionSnapshot`. O snapshot envolve o relatório normalizado de inspeção com proveniência agnóstica de provider e uma chave determinística de idempotência.

## Envelope

Um snapshot serializado contém:

```json
{
  "schemaVersion": "1.3",
  "inspectorVersion": "1.0.0",
  "repositoryIdentity": "github.com/owner/repository",
  "commitSha": "0123456789012345678901234567890123456789",
  "ref": "refs/heads/main",
  "observedAtUtc": "2026-08-20T08:53:12+00:00",
  "execution": {
    "id": "123456789",
    "provider": "github-actions",
    "ref": "refs/heads/main"
  },
  "reportSha256": "<64 caracteres hexadecimais em lowercase>",
  "idempotencyKey": "dri1:<64 caracteres hexadecimais em lowercase>",
  "idempotencyScope": "repositoryState",
  "report": {
    "schemaVersion": "1.3"
  }
}
```

`schemaVersion` é a versão do schema do relatório de inspeção. Ela é repetida intencionalmente no envelope para que um sink consiga rotear/indexar a evidência sem precisar interpretar primeiro o relatório aninhado.

## Criando snapshots

Use `InspectionSnapshotFactory` depois da inspeção:

```csharp
var execution = new InspectionExecutionMetadata(
    Id: runId,
    Provider: "github-actions",
    Ref: "refs/heads/main");

var snapshot = new InspectionSnapshotFactory().Create(
    report,
    inspectorVersion,
    execution);
```

Metadados de execução são opcionais. O modelo não depende de GitHub Actions; exemplos de provider incluem `github-actions`, `gitlab-ci`, `azure-pipelines`, `jenkins` ou `local`.

## Identidade do repositório

Para remotes reconhecidos, a factory normaliza a identidade para `host/path`:

```text
https://github.com/Owner/Repo.git
          -> github.com/Owner/Repo

git@github.com:Owner/Repo.git
          -> github.com/Owner/Repo
```

Credenciais/usuário do transporte e o sufixo `.git` não fazem parte da identidade do repositório.

Quando não é possível derivar um remote canônico, o fallback é `name:<nome-do-repositório>`. Esse valor é útil como proveniência, mas intencionalmente não é considerado forte o bastante para idempotência por estado do repositório.

## Digest do relatório

`reportSha256` calcula o hash do mesmo JSON determinístico e redigido produzido por `InspectionJsonSerializer`.

Isso significa que:

- ordenação de projetos/diagnósticos é normalizada antes do hash;
- contexto sensível de diagnósticos é redigido antes do hash;
- o digest não incorpora intencionalmente credenciais nem output bruto de processos.

## Escopos de idempotência

### `repositoryState`

Usado somente quando:

- a identidade do remote é canônica;
- existe commit SHA;
- o repositório está explicitamente limpo (`isDirty == false`).

A chave permanece estável entre reexecuções da mesma evidência. ID da execução de CI, timestamp da observação, alias de branch/ref e transporte HTTPS versus SSH não criam duplicações por si só.

A chave muda quando a identidade material muda, incluindo repositório, commit, versão do Inspector, schema de inspeção ou fatos avaliados do relatório.

### `observation`

Usado quando não é possível provar uma identidade forte do estado do repositório, incluindo worktrees sujas, commit SHA ausente ou remote canônico ausente.

Quando `execution.id` é informado, a chave de observação usa provider normalizado mais o ID da execução. Isso mantém retries dentro da mesma execução estáveis. Sem ID de execução, `observedAtUtc` discrimina a observação.

O escopo de observação é deliberadamente conservador: evita tratar dois estados locais/sujos potencialmente diferentes como a mesma evidência histórica.

## Reexecuções

Para um repositório Git canônico e limpo, duas execuções podem ter timestamps, IDs de run, branches/refs ou transportes de clone diferentes e ainda resolver para a mesma chave de idempotência quando a evidência canônica é equivalente.

O sink deve usar a chave como chave de upsert/deduplicação. Ele ainda pode manter histórico de execuções separadamente quando o consumidor quiser registrar cada observação.

Para o escopo `observation`, uma execução de CI diferente normalmente produz uma chave diferente.

## Timestamps UTC

`observedAtUtc` é sempre normalizado para UTC pela factory. Consumidores devem armazená-lo como instante UTC, e não convertê-lo para o fuso local do servidor do sink.

## Retenção

O Inspector não define período de retenção. Retenção, compactação, arquivamento e exclusão de snapshots são responsabilidades do sink/consumidor.

## Segurança

Não adicione credenciais, headers de autorização, connection strings ou tokens a `InspectionExecutionMetadata`. Credenciais do sink pertencem ao mecanismo de secrets do host e não são proveniência.

`InspectionSnapshotJsonSerializer` serializa o relatório aninhado por meio de `InspectionJsonSerializer`, preservando as regras de redação de contexto sensível do projeto.

## Decisões relacionadas

- [ADR 0003: Manter a persistência de snapshots opcional atrás de adapters de sink](decisions/0003-persistence-sink-architecture.md)
- [ADR 0004: Definir proveniência e idempotência de snapshots a partir da evidência canônica](decisions/0004-snapshot-provenance-idempotency.md)
- [Persistência opcional](persistence.md)
- [Segurança e privacidade](security.md)
