# ADR 0006: Definir a arquitetura do adapter MCP e o contrato do MVP

**Idiomas:** [English](../../en/decisions/0006-mcp-adapter-architecture.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-19
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

DotNetRepoInspector já possui um núcleo de inspeção estável: `DotNetRepoInspector.Core` mantém os contratos normalizados, `DotNetRepoInspector.Engine` expõe `IRepositoryInspector`, adapters de infraestrutura coletam evidência de Git/MSBuild e a CLI serializa o `InspectionReport` público.

A issue #125 inicia o roadmap do `DotNetRepoInspector.Mcp`. Esta primeira etapa deve definir a arquitetura e o contrato antes que as issues de implementação criem o host, as tools, os testes, o fluxo de distribuição e a documentação para usuários. A superfície MCP não deve se tornar um segundo engine de inspeção, um engine de políticas, um serviço remoto ou uma integração com provider de modelo.

As convenções atuais de MCP/.NET importam para esta decisão porque o ecossistema ainda evolui. A orientação verificada para esta ADR é:

- o MCP C# SDK oficial expõe `ModelContextProtocol` para servidores stdio com hosting/DI e descoberta baseada em atributos;
- um servidor stdio comunica mensagens do protocolo MCP por stdin/stdout e deve enviar logs para stderr;
- servidores MCP NuGet são pacotes .NET tool executados por `dnx`;
- NuGet.org incentiva um manifesto `.mcp/server.json` embutido e o package type `McpServer`;
- o servidor publicado pode ser consumido por clientes MCP como VS Code, Visual Studio, GitHub Copilot coding agent, Claude Code, Cursor e outros hosts compatíveis com o protocolo.

## Specification

### Objetivos

`DotNetRepoInspector.Mcp` deve expor fatos determinísticos de inspeção de repositórios para clientes MCP preservando as fronteiras de domínio existentes:

1. MCP é um adapter de delivery sobre `DotNetRepoInspector.Engine`.
2. `IRepositoryInspector` continua sendo a fonte da verdade para o comportamento de inspeção.
3. O servidor é local, read-only, baseado em stdio e agnóstico de modelo/provider.
4. O repository root é uma fronteira explícita de startup e toda chamada de tool é limitada a essa fronteira.
5. O catálogo de tools é pequeno o suficiente para o MVP, mas estruturado para que tools granulares futuras continuem sendo visões derivadas do mesmo relatório de inspeção.

### Requisitos

- Criar `src/DotNetRepoInspector.Mcp` como projeto host/adapter na issue de implementação.
- Referenciar `DotNetRepoInspector.Engine` e `DotNetRepoInspector.Core`; não referenciar a CLI como camada de composição em runtime.
- Usar o pacote oficial do MCP C# SDK apropriado para hosting stdio (`ModelContextProtocol`) e `Microsoft.Extensions.Hosting`.
- Usar stdio como único transporte do MVP via `.WithStdioServerTransport()`.
- Registrar tools read-only a partir do assembly do adapter MCP.
- Aceitar um repository root explícito no startup do servidor por meio de `--root <path>`.
- Resolver o root de startup para um caminho absoluto existente antes de servir qualquer tool.
- Rejeitar entradas de tool que sejam caminhos absolutos, tentativas de path traversal, caminhos relativos malformados ou caminhos resolvidos fora do root de startup.
- Propagar cancelamento da requisição MCP para `IRepositoryInspector.InspectAsync`.
- Manter payloads do protocolo MCP apenas em stdout/stdin; logs operacionais, diagnósticos de startup do servidor e mensagens do host devem ir para stderr.
- Retornar resultados e erros machine-readable; não exigir parser específico de LLM.
- Preservar `InspectionReport` como payload canônico de inspeção e evitar mudanças no contrato JSON público por esta ADR.

### Restrições arquiteturais

- Não duplicar descoberta, avaliação MSBuild, classificação, metadados Git, serialização JSON ou regras de diagnóstico no projeto MCP.
- Não adicionar SDKs de OpenAI, Anthropic, Google, Gemini, GitHub Copilot ou outro provider de LLM ao código de produto.
- Não adicionar Streamable HTTP, SSE, hospedagem remota, autenticação, RAG, embeddings, geração de código, tools de escrita, efeitos colaterais de persistência ou mutação do repositório ao MVP.
- Não inspecionar conteúdo de arquivos-fonte apenas para atender requisições MCP.
- Não usar silenciosamente um current directory arbitrário como fronteira de confiança.

### Contratos

O adapter MCP possui seu próprio contrato de resultado de tool, mas os dados de inspeção bem-sucedidos continuam sendo o `InspectionReport` existente.

Todas as respostas de tool usam um objeto JSON emitido como conteúdo MCP:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {}
}
```

Erros usam o mesmo envelope com `ok: false`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": false,
  "error": {
    "code": "repository_root_required",
    "message": "The MCP server requires an explicit repository root.",
    "details": {}
  }
}
```

A versão do envelope MCP é independente de `InspectionReport.schemaVersion`. Alterar o envelope de forma incompatível exige uma nova major MCP; alterar `InspectionReport` continua governado pela política existente de compatibilidade do schema de inspeção.

Códigos de erro esperados para o MVP:

| Código | Significado |
| --- | --- |
| `repository_root_required` | O servidor foi iniciado sem repository root explícito. |
| `repository_root_not_found` | O root configurado não existe ou não é um diretório. |
| `path_outside_repository_root` | Um caminho relativo informado foi resolvido fora do root de startup. |
| `invalid_tool_input` | O JSON de entrada é malformado ou viola o schema da tool. |
| `inspection_cancelled` | A requisição MCP ou o shutdown do processo cancelou a inspeção. |
| `inspection_failed` | O engine falhou antes que um `InspectionReport` pudesse ser produzido. |
| `project_not_found` | Uma tool específica de projeto não encontrou o caminho de projeto relativo ao repositório. |

## Plan

### Direção de dependências

O projeto MCP é um adapter na borda de delivery:

```text
MCP client
   |
   v
DotNetRepoInspector.Mcp
   |
   v
DotNetRepoInspector.Engine
   |
   +--> DotNetRepoInspector.Core
   +--> DotNetRepoInspector.MSBuild
   +--> DotNetRepoInspector.Git
```

`DotNetRepoInspector.Mcp` pode depender de hosting, do MCP C# SDK, da Engine e dos contratos do Core. Core não deve depender de MCP. Engine não deve conhecer transportes MCP, nomes de tools, envelopes de erro MCP, clientes ou providers de modelo.

### Fronteira de confiança

O repository root configurado é o escopo máximo de filesystem para entradas das tools MCP. O adapter valida caminhos antes de chamar o engine ou derivar uma visão. A fronteira limita argumentos acidentais ou maliciosos de tools, mas não é um sandbox para avaliação MSBuild. Repositórios não confiáveis continuam exigindo execução isolada, efêmera, non-privileged e sem secrets, como documentado para CLI/container.

O MVP não concede acesso de escrita. Nenhuma tool MCP cria, edita, exclui, formata, restaura, commita, persiste ou faz upload de arquivos do repositório.

### Transporte e comportamento do host

O MVP usa apenas stdio. Isso se alinha ao uso local de MCP, à execução via NuGet/dnx e a clientes de IDE/agente que iniciam um processo filho. Streamable HTTP fica explicitamente fora de escopo até que uma ADR futura reavalie hospedagem remota, autenticação, validação de host, CORS e exposição operacional.

O host deve configurar logging para que todos os logs vão para stderr. Qualquer escrita em stdout fora do transporte MCP é bug de protocolo.

### Catálogo de tools

O catálogo inicial de tools v1 é:

| Tool | Objetivo | Issue de implementação |
| --- | --- | --- |
| `inspect_repository` | Executar `IRepositoryInspector` para o root configurado e retornar o `InspectionReport` canônico completo. | #127 |
| `list_projects` | Retornar um índice compacto de projetos derivado do mesmo relatório de inspeção: path, name, target frameworks, classification e contagens de diagnósticos. | #128 |
| `get_project_details` | Retornar um `ProjectInspection` por caminho de projeto relativo ao repositório, derivado de um relatório de inspeção. | #128 |
| `get_project_reference_graph` | Retornar arestas de referências entre projetos e diagnósticos de referências não resolvidas. | #128 |
| `get_repository_diagnostics` | Retornar diagnósticos de repositório e de projetos com o contexto de caminho do projeto. | #128 |
| `get_sdk_metadata` | Retornar metadata do .NET SDK configurado e resolvido. | #128 |

Nenhum prompt ou resource faz parte do MVP. As tools são intencionalmente read-only e determinísticas; prompts no cliente podem decidir como usar os fatos, mas o servidor não pede que um modelo os interprete.

### Schemas de entrada e saída das tools

Opções comuns de inspeção aceitas por tools que executam ou derivam uma inspeção:

```json
{
  "type": "object",
  "properties": {
    "configurationPath": {
      "type": "string",
      "description": "Optional repository-relative path to a DotNetRepoInspector configuration file."
    },
    "disableConfigurationFile": {
      "type": "boolean",
      "default": false
    },
    "excludedPaths": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Optional repository-relative project paths to exclude."
    },
    "classificationOverrides": {
      "type": "object",
      "additionalProperties": { "type": "string" }
    }
  },
  "additionalProperties": false
}
```

Saída de `inspect_repository`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": "InspectionReport"
  }
}
```

Saída de `list_projects`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "inspectionSchemaVersion": "1.3",
    "projects": [
      {
        "path": "src/App/App.csproj",
        "name": "App",
        "targetFrameworks": ["net10.0"],
        "classification": {
          "kind": "web",
          "confidence": "high"
        },
        "diagnosticCount": 0,
        "errorCount": 0,
        "warningCount": 0
      }
    ]
  }
}
```

`get_project_details` adiciona uma entrada obrigatória:

```json
{
  "projectPath": "src/App/App.csproj"
}
```

Sua saída contém um único `ProjectInspection` em `data.project`.

`get_project_reference_graph` retorna `data.projects`, em que cada item contém o `path` do projeto, suas `references` canônicas e `diagnostics` de referências não resolvidas.

Saída de `get_repository_diagnostics`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "inspectionSchemaVersion": "1.3",
    "diagnostics": [
      {
        "projectPath": null,
        "diagnostic": "InspectionDiagnostic"
      }
    ]
  }
}
```

`get_sdk_metadata` retorna o `DotNetSdkMetadata` canônico em `data.dotNetSdk`. Toda resposta granular também carrega `data.inspectionSchemaVersion`, permitindo que consumidores rastreiem os fatos projetados até o contrato versionado de `InspectionReport`. Inspeções vazias e parciais recuperáveis são resultados bem-sucedidos com coleções vazias, fatos de SDK anuláveis ou diagnósticos canônicos; não são erros do adapter.

### Versionamento e identidade do pacote

O servidor MCP possui uma identidade NuGet separada:

```text
PackageId: DotNetRepoInspector.Mcp
Tool command: dotnet-repo-inspector-mcp
MCP package type: McpServer
MCP registry name: io.github.rodri-oliveira-dev/dotnet-repo-inspector-mcp
```

O pacote deve ser versionado em lockstep com a release do repositório/produto, salvo se uma ADR futura de release definir uma cadência separada. Versionamento em lockstep mantém CLI, Action, container, contrato JSON e fatos MCP atribuíveis à mesma revision de código-fonte.

Para o MVP, o pacote NuGet deve ser framework-dependent em vez de self-contained. O servidor já exige um ambiente compatível com .NET SDK para `dnx`, e inspeção de repositórios exige disponibilidade de SDK/MSBuild que um runtime self-contained não resolveria. Empacotamento self-contained ou native AOT pode ser reavaliado somente se evidência de distribuição mostrar benefício concreto e o contrato de compatibilidade MSBuild/SDK permanecer intacto.

### Estratégia de distribuição

O pacote é distribuído como pacote NuGet .NET tool executável por `dnx`, com manifesto `.mcp/server.json` embutido e `PackageType` `McpServer`. O manifesto deve descrever transporte stdio, identidade do pacote, versão do pacote, URL do repositório e um input obrigatório de startup para repository root.

A publicação deve usar o pipeline protegido de release e o modelo de Trusted Publishing já estabelecidos no repositório. Esta ADR não publica um pacote nem altera o pipeline de release; a implementação pertence às issues dedicadas de distribuição.

### Compatibilidade multi-cliente

Compatibilidade é definida na fronteira do protocolo MCP e do processo local stdio. Codex, Claude Code, Gemini CLI, VS Code, Visual Studio, GitHub Copilot coding agent, Cursor e clientes similares são consumidores da mesma superfície de protocolo. Código de produto não deve ramificar por provider de LLM, incluir SDKs de provider ou embutir prompts específicos de provider como comportamento de runtime.

Testes de compatibilidade de clientes podem usar hosts diferentes, mas esses testes validam interoperabilidade de protocolo e seleção de tools; eles não alteram a arquitetura do servidor.

## Tasks

O roadmap decompõe esta ADR em trabalho de implementação:

1. #126 cria o projeto host stdio, opções de startup, composição DI, logging em stderr, wiring de cancelamento e validação de repository root.
2. #127 implementa `inspect_repository` sobre `IRepositoryInspector` e comprova o round trip do `InspectionReport` canônico.
3. #128 implementa as tools granulares derivadas sem duplicar lógica de inspeção.
4. #129 adiciona testes de protocolo/E2E que exercitam stdio sem rede, secrets ou providers de modelo.
5. #130 endurece o comportamento de fronteira de paths e confiança.
6. #131 cobre confiabilidade, observabilidade, cancelamento, timeout e guardrails de performance.
7. #132 adiciona quality gates de CI para o servidor MCP.
8. #133 e #139 validam comportamento multi-cliente como evidência de protocolo, não acoplamento de produto.
9. #134 cria documentação MCP para usuários nos dois idiomas.
10. #135 e #136 implementam empacotamento NuGet/dnx e publicação protegida.
11. #137 e #138 entregam prerelease/GA somente depois que a evidência anterior estiver completa.

## Implementation

A issue #125 está completa quando esta ADR existe nos dois idiomas, está indexada e está refletida na visão geral de arquitetura. Nenhum host ou tool MCP é implementado por esta issue. Nenhum campo de `InspectionReport` é adicionado, removido ou renomeado.

## Alternativas consideradas

### Colocar as tools MCP no projeto da CLI

Rejeitada. A CLI mantém parsing de argumentos, emissão JSON em stdout, composição de persistência e exit codes. Um servidor MCP tem responsabilidades diferentes de transporte e protocolo. Compartilhar o engine está correto; compartilhar o host da CLI acoplaria dois protocolos de delivery e aumentaria o risco de poluição de stdout.

### Tornar MCP a nova camada de orquestração de inspeção

Rejeitada. `IRepositoryInspector` já centraliza o workflow de inspeção e a semântica de falhas. Duplicá-lo no MCP criaria comportamento inconsistente entre CLI, Action, container e MCP.

### Usar Streamable HTTP no MVP

Rejeitada. HTTP é útil para cenários remotos/de servidor, mas introduz autenticação, validação de host, CORS, ciclo de vida e preocupações de deployment que ficam fora do MVP local read-only. stdio se alinha ao uso local por agentes e IDEs.

### Acoplar compatibilidade a providers de LLM nomeados

Rejeitada. O produto deve ser compatível com o protocolo, não específico de provider. SDKs de provider adicionariam dependências e fronteiras de confiança desnecessárias sem entregar valor determinístico de inspeção.

### Entregar primeiro um pacote MCP self-contained

Rejeitada para o MVP. Isso aumenta tamanho do pacote e complexidade de release sem remover a necessidade de toolchain .NET SDK/MSBuild para inspecionar repositórios corretamente.

## Consequências

### Positivas

- MCP se torna um adapter sobre o engine existente em vez de um segundo núcleo de produto.
- A fronteira de root, o escopo read-only e a regra de não usar SDK de provider ficam explícitos antes da implementação.
- O catálogo de tools é pequeno, mas cobre workflows de relatório completo e workflows focados para agentes.
- A distribuição NuGet/dnx se alinha às convenções atuais de MCP em .NET.
- `InspectionReport` permanece estável e reutilizável entre CLI, Action, container, persistência e MCP.

### Trade-offs

- O MVP apenas stdio não atende clientes remotos sem uma ponte de processo local.
- Empacotamento framework-dependent exige um ambiente compatível com .NET SDK/dnx.
- Tools derivadas podem reexecutar inspeção até que uma implementação futura adicione cache em processo com escopo limitado e invalidação clara.
- Validação de repository root reduz alcance acidental, mas não torna avaliação MSBuild segura para repositórios hostis.

## Referências

- Issue #125: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/125
- Roadmap #140: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/140
- MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- Orientação de transportes do MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md
- Getting started do MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md
- Microsoft Learn: MCP servers in NuGet packages: https://learn.microsoft.com/nuget/concepts/nuget-mcp
- Microsoft Learn: Create a minimal MCP server using C# and publish to NuGet: https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server
- Microsoft Learn: Publish an MCP server on NuGet.org to the Official MCP Registry: https://learn.microsoft.com/dotnet/ai/quickstarts/publish-mcp-registry
- Engine de inspeção: [`../inspection-engine.md`](../inspection-engine.md)
- Modelo de segurança: [`../security.md`](../security.md)
- Release/versionamento: [`../releases.md`](../releases.md)
