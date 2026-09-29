# ADR 0017: Definir a fronteira de extensibilidade da policy engine

**Idiomas:** [English](../../en/decisions/0017-policy-engine-extensibility-boundary.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-29
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O DotNetRepoInspector precisa de regras opt-in de governança sem transformar inspeção em enforcement de policy nem duplicar semântica de regras entre CLI, GitHub Action, MCP, persistência ou futuros adapters de delivery.

A engine de inspeção já produz contratos normalizados do Core depois que evidências de Git e MSBuild são coletadas. A avaliação de policies, portanto, precisa de uma fronteira estável de extensibilidade que consuma esses fatos normalizados, preserve comportamento determinístico e mantenha findings de policy distintos de diagnostics da inspeção.

A primeira rule concreta é a policy de TargetFramework da issue #157, mas a fronteira deve suportar regras adicionais sem acoplar o Core ao MSBuild ou a um mecanismo de delivery.

## Decisão

A extensibilidade de policies pertence a `DotNetRepoInspector.Core.Policies`.

### Responsabilidade das rules

- `IPolicyRule` é o contrato público de rule.
- Rules consomem apenas `PolicyEvaluationContext`, construído a partir dos contratos normalizados de inspeção do Core.
- Rules não devem depender de MSBuild, Git, CLI, GitHub Actions, transportes MCP, adapters de persistência ou parsing de arquivos-fonte do repositório.
- Uma rule é responsável pelo seu código estável `DRPxxxx` e emite findings estruturados; ela não decide exit codes de processo nem comportamento de delivery.

### Registro e configuração

- A camada de Engine/configuração é responsável por traduzir configuração validada do repositório em instâncias explícitas de rules.
- O registro de rules é explícito. O produto não descobre rules por reflection, assembly scanning, registries globais estáticos ou convenções de adapters de delivery.
- Sem configuração não existem policies registradas. A inspeção permanece zero-config e sem policies por padrão.
- O versionamento do schema de configuração é independente do versionamento do schema do relatório de inspeção.

### Ciclo de avaliação

- `RepositoryInspector` conclui primeiro a inspeção normal e cria o `InspectionReport` normalizado.
- Se nenhuma rule estiver registrada, o relatório é retornado sem avaliação de policy.
- Se houver rules registradas, `PolicyEngine` as avalia depois da inspeção usando somente fatos normalizados.
- A avaliação de policy não altera projetos, diagnostics, classificação, fatos de SDK, referências nem outras evidências da inspeção.

### Ordenação e determinismo

- `PolicyEngine` rejeita códigos de rule duplicados.
- Rules são avaliadas na ordem explícita de registro.
- Findings produzidos por cada rule preservam a ordem determinística de avaliação daquela rule.
- O serializer público canonicaliza `policyFindings` para que fatos equivalentes produzam JSON estável independentemente de ordenações incidentais de coleções.

### Findings e diagnostics

- Resultados de policy são representados por `policyFindings` no nível superior.
- Falhas e problemas recuperáveis da inspeção permanecem valores `InspectionDiagnostic` em `diagnostics` ou `projects[].diagnostics`.
- Uma violação de policy nunca deve ser convertida em diagnostic de inspeção apenas para influenciar um exit code.
- Adapters de delivery podem derivar status de execução a partir dos findings. CLI e GitHub Action retornam exit code `1` para finding de policy `error`, enquanto um `warning` de policy não falha uma inspeção que esteja saudável.

### Responsabilidades dos adapters

- A CLI serializa o relatório canônico e aplica a semântica documentada de exit code; ela não implementa lógica de rules.
- A GitHub Action encaminha o contrato existente de configuração para a CLI e reutiliza o mesmo relatório e exit code; ela não adiciona um segundo parser de policy.
- MCP, persistência, containers e futuros adapters consomem o mesmo relatório normalizado e não devem redefinir a semântica de policies.

## Alternativas consideradas

### Implementar policies na CLI ou na GitHub Action

Rejeitada. Isso criaria múltiplas policy engines, tornaria o comportamento específico de cada delivery e permitiria divergência entre resultados locais da CLI e do CI.

### Permitir que rules consumam tipos brutos do MSBuild ou arquivos de projeto

Rejeitada. As rules ficariam acopladas a infraestrutura e detalhes de coleta em vez dos fatos normalizados e estáveis expostos pelo Core.

### Misturar violações de policy aos diagnostics da inspeção

Rejeitada. Uma decisão de governança é semanticamente diferente de um problema de inspeção. Misturá-las impediria consumidores de distinguir saúde do repositório de conformidade de policy.

### Descobrir rules dinamicamente por reflection ou registry global

Rejeitada para a fronteira atual do produto. Construção explícita a partir de configuração validada é mais simples de raciocinar, testar, ordenar deterministicamente e proteger.

## Consequências

### Positivas

- A inspeção permanece determinística e útil sem policies.
- Novas rules podem estender governança sem alterar a coleta MSBuild ou adapters de delivery.
- Conformidade de policy e saúde da inspeção permanecem independentes e legíveis por máquina.
- CLI e GitHub Action permanecem alinhadas porque reutilizam a mesma Engine e o mesmo relatório.
- A direção de dependências continua centrada no Core e independente de delivery.

### Trade-offs

- Adicionar uma rule configurável exige mapeamento explícito de registro/configuração na Engine além da implementação da rule no Core.
- A fronteira atual favorece rules built-in em vez de descoberta dinâmica de plugins em runtime.
- Consumidores interessados na intenção de policy precisam considerar a configuração além dos findings, pois uma coleção `policyFindings` vazia pode significar que nenhuma policy habilitada produziu finding ou que nenhuma policy foi habilitada.

## Referências

- Issue #28: núcleo da policy engine
- Issue #157: policy de TargetFramework e configuração
- Issue #158: integração com CLI e contrato de inspeção
- Issue #159: integração com GitHub Action e E2E
- Schema de inspeção: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Configuração: [`../configuration.md`](../configuration.md)
- GitHub Action: [`../github-action.md`](../github-action.md)
