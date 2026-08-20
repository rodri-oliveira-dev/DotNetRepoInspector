# Arquitetura

**Idiomas:** [English](../../en/architecture/README.md) | Português (Brasil)

Fronteiras atuais:

- **Core** — modelo normalizado de inspeção e conceitos de classificação. Não possui dependência de Git, MSBuild, provedores de CI ou persistência.
- **MSBuild** — descoberta de projetos e extração de metadados MSBuild avaliados.
- **Git** — adapter de estado do repositório que descobre a work tree do Git e coleta metadados normalizados do repositório por meio do executável `git`.
- **Engine** — orquestração da inspeção no nível da aplicação. Compõe metadados Git, inspeção de SDK, descoberta de projetos, fatos MSBuild avaliados, classificação, referências e diagnósticos em um `InspectionReport` estável.
- **Persistence** — contrato opcional de sinks posterior à inspeção e política de publicação independente de provider. Depende apenas de Core e nunca é chamado implicitamente pela Engine.
- **CLI** — composição da linha de comando, serialização e semântica de exit code.
- **Integrations** — adapters de delivery, como GitHub Actions, além de futuros sinks concretos de persistência e camadas de políticas/relatórios.

A direção das dependências é intencionalmente unidirecional:

```text
                 Delivery / integrations
                    |             |
                    v             v
                  Engine      Persistence
               /    |    \          |
             Git  MSBuild  Core <----+
              \     |     /
                  Core
```

`Core` permanece agnóstico de infraestrutura. `Engine` pode depender dos adapters de infraestrutura necessários para inspecionar um repositório, mas não conhece detalhes de linha de comando, GitHub Actions, persistência ou qualquer outro mecanismo de delivery.

Persistência é composta por um host somente depois que um `InspectionReport` existe. Assim, indisponibilidade de transporte, credenciais, estado de retry e falhas específicas de sink nunca se tornam fatos de inspeção.

O comportamento ponta a ponta da inspeção, incluindo a semântica de falhas parciais e fatais da inspeção, está documentado em [`../inspection-engine.md`](../inspection-engine.md). Persistência opcional é documentada separadamente em [`../persistence.md`](../persistence.md) e na [ADR 0003](../decisions/0003-persistence-sink-architecture.md).
