# Readiness de disponibilidade geral do MCP

**Idiomas:** [English](../en/mcp-ga-readiness.md) | Português (Brasil)

Este documento é o SDD e o registro de evidências da issue #138. O primeiro pacote MCP estável planejado é **`DotNetRepoInspector.Mcp` `1.2.0`**, versionado em lockstep com o repositório. Ele não está publicado e não pode ser descrito como geralmente disponível enquanto os gates obrigatórios abaixo permanecerem abertos.

## Specification

O GA exige RC concluído e publicado, zero findings críticos/altos sem tratamento, documentação bilíngue e evidências de compatibilidade atuais e contrato machine-readable de readiness verde. O pacote estável deve ser publicado somente pelo workflow protegido de Release a partir de uma ref permitida, com Trusted Publishing e aprovação humana obrigatória. Depois, o pacote exato do NuGet.org deve passar por `dnx`, handshake MCP, discovery, chamadas principais, dois clientes reais de providers, hashes, attestations, provenance e smokes pós-publicação.

O catálogo público v1 está congelado para `1.2.0`:

| Tool | Contrato estável |
| --- | --- |
| `inspect_repository` | Envelope canônico de `InspectionReport` |
| `list_projects` | Fatos compactos e determinísticos de projetos |
| `get_project_details` | Um projeto por path relativo ao repositório |
| `get_project_reference_graph` | Arestas de referência e diagnostics não resolvidos |
| `get_repository_diagnostics` | Fatos de diagnostics do repositório e projetos |
| `get_sdk_metadata` | Metadados de SDK configurado e resolvido |

Inputs, outputs, envelopes de erro, `mcpSchemaVersion` `1.0`, annotations read-only, transporte stdio e `--root` explícito permanecem como documentado no [guia MCP](mcp.md) e no [contrato do servidor](architecture/mcp-server.md). Nenhuma mudança incompatível de contrato faz parte da preparação do GA.

## Plan

1. Revisar os findings do RC e manter o GA bloqueado até a conclusão da #137.
2. Validar `1.2.0` localmente e em feed controlado e executar todos os gates do repositório.
3. Fazer merge do PR consolidado somente após reviews e checks obrigatórios.
4. Confirmar a policy de Trusted Publishing no NuGet.org e a aprovação do environment protegido `release`.
5. Disparar o workflow de Release da ref permitida com versão `1.2.0` e `publish=true`.
6. Verificar NuGet.org, `dnx` com versão exata, dois providers, assets, manifest, hashes, attestations, evidências de container e scans pós-publicação.
7. Marcar GA e roadmap como concluídos somente depois de anexar todas as evidências à #138.

## Tasks e evidências atuais

| Gate | Estado | Evidência ou dependência |
| --- | --- | --- |
| Revisão do RC | Bloqueado | #137 permanece aberta; seus findings bloqueadores não foram resolvidos |
| Segurança | Preparado | Suítes de segurança e superfícies de alertas do GitHub são verificadas; publicação exige nova execução protegida |
| Congelamento de contrato | Concluído | Seis tools listadas acima e no readiness machine-readable |
| Metadados do pacote estável | Preparado | `1.2.0` pode ser empacotado e inspecionado; evidência do NuGet.org não existe |
| Compatibilidade | Bloqueado | Codex está validado; segundo provider real continua exigido por #133/#139 |
| Performance/confiabilidade | Preparado | `.github/mcp-performance-baseline.json`, fila limitada, cancelamento, timeout e telemetria em stderr |
| Distribuições existentes | Preparado | CLI, Action e container permanecem no workflow protegido em lockstep; #106 não está concluída |
| Supply chain | Bloqueado | Hashes, attestations, provenance, SBOM e manifest oficiais exigem a publicação protegida |
| Publicação | Bloqueado | Merge/ref permitida, aprovação de environment e policy Trusted Publishing são gates externos |

A medição da candidata a GA em Windows x64/.NET 10 registrou 68 ms de process launch, 968 ms de startup mais handshake, 11,43 s de inspeção da Engine, 11,75 s de inspeção pela CLI e chamadas MCP entre 10,38 s e 11,22 s para a fixture `ProjectKinds` de seis projetos. Todas as relações e budgets fixos passaram. Esses valores são evidência de regressão para a fixture controlada, não um SLA para repositórios arbitrários.

## Implementation

`.github/release-readiness-v1.json` registra versão estável, tools congeladas, baseline, documentação, issues bloqueadoras e controles externos. Testes exigem que o estado permaneça `blocked` até uma mudança deliberada de evidência. As release notes preliminares ficam em [`mcp-1.2.0-release-notes.md`](mcp-1.2.0-release-notes.md).

## Bloqueios do GA

- #106: baseline do release de container em lockstep está incompleto.
- #133 e #139: não existem segundo cliente de provider e evidência de eval multi-provider.
- #137: nenhum RC foi publicado, portanto não existem pacote público exato e evidências pós-publicação.
- A policy de Trusted Publishing do `DotNetRepoInspector.Mcp` no NuGet.org não está confirmada.
- O PR consolidado não foi integrado a uma ref permitida, e a aprovação do environment protegido não ocorreu.

Esses são gates obrigatórios, não candidatos a vNext. Trabalhos pós-v1 existentes, como classificações mais ricas (#25) e políticas opcionais (#28), permanecem fora do GA e não alteram o contrato MCP v1 congelado.
