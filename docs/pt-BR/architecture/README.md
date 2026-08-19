# Arquitetura

**Idiomas:** [English](../../en/architecture/README.md) | Português (Brasil)

Fronteiras atuais:

- **Core** — modelo normalizado de inspeção e conceitos de classificação. Não possui dependência de Git, MSBuild, provedores de CI ou persistência.
- **MSBuild** — descoberta de projetos e extração de metadados MSBuild avaliados.
- **Git** — adapter de estado do repositório que descobre a work tree do Git e coleta metadados normalizados do repositório por meio do executável `git`.
- **Engine** — orquestração da inspeção no nível da aplicação. Compõe metadados Git, inspeção de SDK, descoberta de projetos, fatos MSBuild avaliados, classificação, referências e diagnósticos em um `InspectionReport` estável.
- **CLI** — composição da linha de comando, serialização e semântica de exit code.
- **Integrations** — futuros adapters, como GitHub Actions, sinks de persistência e camadas de políticas/relatórios.

A direção das dependências é intencionalmente unidirecional:

```text
Delivery / integrations
        ↓
      Engine
   ↙    ↓    ↘
 Git  MSBuild  Core
  ↘     ↓     ↙
       Core
```

`Core` permanece agnóstico de infraestrutura. `Engine` pode depender de adapters de infraestrutura para orquestrar uma inspeção, mas não conhece detalhes de linha de comando, GitHub Actions, persistência ou qualquer outro mecanismo de delivery.

O comportamento ponta a ponta da inspeção, incluindo a semântica de falhas parciais e fatais, está documentado em [`../inspection-engine.md`](../inspection-engine.md).
