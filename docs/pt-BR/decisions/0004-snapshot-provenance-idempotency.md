# ADR 0004: Definir proveniência e idempotência de snapshots a partir da evidência canônica

**Idiomas:** [English](../../en/decisions/0004-snapshot-provenance-idempotency.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-08-20
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

Evidências de inspeção persistidas precisam ser atribuíveis ao estado do repositório, à versão do Inspector e à execução que as produziu. Reexecutar o mesmo commit limpo não deve criar duplicações acidentais, enquanto estados ambíguos, como worktrees com alterações locais, não podem ser tratados como se o commit SHA os descrevesse por completo.

O modelo deve permanecer agnóstico de provider e não pode depender de identificadores específicos do GitHub Actions.

## Decisão

`InspectionSnapshot` é o envelope de evidência entregue aos sinks de persistência. Ele carrega:

- `schemaVersion` da inspeção;
- `inspectorVersion`;
- `repositoryIdentity` canônica;
- `commitSha` quando disponível;
- `ref` quando disponível;
- `observedAtUtc`;
- metadados genéricos opcionais da execução (`id`, `provider`, `ref`);
- SHA-256 do relatório de inspeção normalizado e redigido;
- uma chave de idempotência versionada;
- o escopo de idempotência;
- o `InspectionReport` original.

`InspectionSnapshotFactory` é o único componente agnóstico de provider responsável por produzir esse envelope.

### Identidade do repositório

Quando existe um remote Git reconhecido, a identidade do repositório é normalizada para `host/path` e não preserva transporte/usuário nem o sufixo `.git`. Remotes equivalentes em HTTPS e SSH no formato SCP passam, portanto, a resolver para a mesma identidade.

Se não for possível derivar um remote canônico, o nome do repositório é preservado como `name:<nome>` apenas como proveniência descritiva. Esse fallback não é considerado forte o bastante para idempotência baseada no estado do repositório.

### Digest do relatório

`reportSha256` é SHA-256 sobre `InspectionJsonSerializer.Serialize(report)`. Isso reutiliza a ordenação determinística e a redação de contexto sensível já existentes antes do hash, impedindo que secrets sejam incorporados ao digest.

### Idempotência por estado do repositório

Um snapshot usa o escopo `RepositoryState` somente quando todas as condições abaixo são verdadeiras:

1. existe uma identidade canônica de remote;
2. existe commit SHA;
3. `repository.isDirty` é explicitamente `false`.

A chave de idempotência é `dri1:<sha256>` calculada sobre uma entrada canônica versionada contendo identidade do repositório, commit, versão do Inspector, versão do schema de inspeção e um digest canônico do relatório.

Nesse escopo, aliases mutáveis e dados da execução não participam da identidade:

- branch/ref é removida do digest canônico;
- transporte HTTPS versus SSH é normalizado;
- ID da execução de CI e timestamp são ignorados.

Assim, uma reexecução da mesma evidência limpa pode ser tratada com upsert usando a mesma chave. Alterações de versão do Inspector, schema, commit, repositório ou fatos avaliados materialmente diferentes geram outra chave.

### Idempotência por observação

Um snapshot usa o escopo `Observation` quando a identidade do estado do repositório não é forte o bastante, incluindo:

- worktree suja;
- commit SHA ausente;
- identidade de remote canônica ausente ou não reconhecida.

Chaves de observação incluem o digest normalizado do relatório e um discriminador de execução. Quando o consumidor informa um ID de execução, `provider + execution id` é usado para que retries dentro da mesma execução de CI permaneçam estáveis. Caso contrário, `observedAtUtc` é usado.

O escopo `Observation` não afirma que duas execuções representam o mesmo estado do repositório apenas porque compartilham o mesmo commit SHA.

### Proveniência da execução

Os metadados da execução são genéricos:

```text
id       identificador opcional da execução/run
provider produtor opcional, como github-actions, gitlab-ci, azure-pipelines, jenkins, local
ref      ref completa opcional, como refs/heads/main ou refs/tags/v1.2.3
```

O provider é normalizado para lowercase. A ref explícita da execução tem precedência sobre a branch capturada pelo Git no campo `ref` do snapshot.

### Timestamps UTC

`observedAtUtc` é obtido por `TimeProvider` e normalizado para UTC antes de ser armazenado. `InspectionSnapshotJsonSerializer` serializa o envelope de evidência com `System.Text.Json` usando contrato camelCase estável.

### Retenção

Retenção, compactação histórica e exclusão permanecem responsabilidade do sink/consumidor. O Inspector define apenas identidade da evidência e semântica de replay.

## Alternativas consideradas

### Apenas commit SHA

Rejeitado. O mesmo commit pode produzir evidências diferentes com outras versões do Inspector/schema ou ambientes de SDK resolvidos, e uma worktree suja não é completamente representada pelo HEAD.

### Branch/ref como identidade

Rejeitado. Branches e tags são aliases mutáveis e não podem ser a identidade principal de evidência histórica.

### Timestamp como chave única universal

Rejeitado. Isso impede deduplicação útil de reexecuções limpas e transforma retries inofensivos em duplicações.

### ID da execução de CI como chave principal

Rejeitado. Isso acopla o modelo ao produtor e torna uso local/fora de CI um cenário de segunda classe.

### Hash de output bruto ou estado não redigido

Rejeitado. Output bruto de MSBuild/Git não é o contrato público normalizado e pode conter material instável ou sensível.

## Consequências

### Positivas

- reexecuções limpas podem ser deduplicadas deterministicamente;
- estados ambíguos/sujos não são colapsados incorretamente;
- identidade do snapshot não depende de GitHub Actions;
- transportes Git equivalentes compartilham identidade de repositório;
- hashes do relatório herdam normalização determinística e redação;
- sinks recebem metadados explícitos adequados a índices, upserts e trilhas de auditoria.

### Trade-offs

- idempotência por estado exige remote canônico e commit limpo;
- repositórios locais sem remote caem para identidade por observação;
- uma chave de observação não prova identidade de conteúdo para arquivos arbitrários sujos que não estejam representados no relatório;
- sinks continuam responsáveis por armazenamento, índices e retenção.

## Segurança

Nenhuma credencial ou material de autenticação faz parte da proveniência ou da chave de idempotência. A normalização de remote utiliza apenas identidade canônica `host/path`, e o hash do relatório ocorre depois da redação de contextos sensíveis pelo serializer de inspeção.

## Trabalho futuro

- **#22** — usar `InspectionSnapshotJsonSerializer` e `IdempotencyKey` no primeiro sink HTTP/webhook, incluindo retry transitório limitado e semântica de upsert/replay específica do destino.

## Referências

- Arquitetura de persistência: [ADR 0003](0003-persistence-sink-architecture.md)
- Contrato de proveniência: [`../snapshot-provenance.md`](../snapshot-provenance.md)
- Schema de inspeção: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Modelo de segurança: [`../security.md`](../security.md)
