# Documentação

**Idiomas:** [English](../en/README.md) | Português (Brasil)

Mantenha o [`README.md`](../../README.pt-BR.md) da raiz como ponto de entrada do projeto e coloque aqui o material de design mais detalhado.

- [`architecture/`](architecture/) — arquitetura atual, contratos e design de implementação.
- [`classification.md`](classification.md) — regras determinísticas de classificação de projetos e sua precedência.
- [`configuration.md`](configuration.md) — configuração opcional do repositório, exclusões, overrides de classificação e precedência.
- [`diagnostics.md`](diagnostics.md) — catálogo estável de diagnósticos e regras de logging operacional.
- [`security.md`](security.md) — limite de coleta de dados, modelo de confiança do MSBuild, tratamento de secrets, permissões da Action e credenciais de sinks.
- [`persistence.md`](persistence.md) — persistência opcional de snapshots, contrato de extensão de sinks, timeout, modo de falha e fronteira de retry.
- [`snapshot-provenance.md`](snapshot-provenance.md) — identidade da evidência, proveniência UTC, digest do relatório e semântica de idempotência.
- [`inspection-engine.md`](inspection-engine.md) — orquestração ponta a ponta da inspeção, semântica de falhas, determinismo e cancelamento.
- [`cli.md`](cli.md) — uso da linha de comando, streams de saída, códigos de saída e comportamento de cancelamento.
- [`github-action.md`](github-action.md) — inputs, outputs, bootstrap de runtime, permissões e validação no CI da GitHub Action reutilizável.
- [`compatibility.md`](compatibility.md) — matriz de compatibilidade suportada entre SDK/TFM do .NET e sistemas operacionais.
- [`performance.md`](performance.md) — baseline sintética para repositórios grandes, hotspots medidos e guardrails de regressão.
- [`real-repository-validation.md`](real-repository-validation.md) — harness de validação com repositórios públicos fixados por commit e política de reprodução de bugs.
- [`project-reference-graph.md`](project-reference-graph.md) — semântica normalizada do grafo de `ProjectReference`.
- [`schema/inspection-v1.md`](schema/inspection-v1.md) — contrato JSON público e política de compatibilidade.
- [`decisions/`](decisions/) — Architecture Decision Records para decisões técnicas duradouras.

Enquanto o projeto estiver em desenvolvimento inicial, a documentação deve distinguir o comportamento atual do comportamento planejado.
