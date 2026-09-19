# Servidor MCP

**Idiomas:** [English](../../en/architecture/mcp-server.md) | Português (Brasil)

`DotNetRepoInspector.Mcp` é o adapter local de delivery por stdio definido pela [ADR 0006](../decisions/0006-mcp-adapter-architecture.md). Ele delega a inspeção do repositório a `IRepositoryInspector`; não contém uma segunda implementação de descoberta, avaliação MSBuild, classificação ou Git.

## Specification

### Requisitos funcionais

- O startup exige exatamente um diretório de repositório existente por meio de `--root <path>` ou `--root=<path>`.
- O root absoluto normalizado é imutável durante a vida do processo e constitui o escopo máximo do filesystem aceito nos argumentos de tools.
- O servidor usa somente stdio e publica o nome `DotNetRepoInspector.Mcp` e a versão do assembly do produto durante a negociação MCP.
- O MVP publica `inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics` e `get_sdk_metadata`. Cada tool mapeia opções para o `RepositoryInspectionRequest` existente; as tools granulares apenas projetam visões focadas a partir do `InspectionReport` canônico.
- O cancelamento do cliente é propagado a `IRepositoryInspector.InspectAsync`. Fechar stdin encerra o servidor de forma graciosa.

### Requisitos não funcionais

- stdout é reservado para mensagens do protocolo MCP. Logs operacionais do Generic Host e do MCP usam stderr.
- O servidor e a tool são read-only, determinísticos para o mesmo estado/toolchain do repositório, agnósticos de modelo e livres de SDKs de providers de LLM.
- Caminhos absolutos, vazios e relativos que resolvam fora do root configurado são rejeitados antes da chamada ao Engine.
- Erros esperados expõem códigos estáveis e mensagens sanitizadas, nunca texto de exceptions, stack traces, valores de variáveis de ambiente ou secrets.
- Processos filhos de Git e .NET/MSBuild recebem stdin fechado para que não consumam nem retenham o stream do protocolo MCP.

## Plan

O host executável é responsável pelo parsing de startup, validação do root, DI, metadata MCP, transporte stdio, logs em stderr e ciclo de vida do processo. `RepositoryInspectionExecutor` centraliza a validação do adapter e a chamada única a `IRepositoryInspector`; os handlers completo e granulares mapeiam seu resultado para os schemas declarados. `RepositoryInspector` continua sendo o único orquestrador da inspeção e `InspectionJsonSerializer` continua sendo a fronteira de serialização canônica do report completo.

Os testes são divididos em testes rápidos do parser/handler e testes de protocolo no nível do processo. Os testes de protocolo iniciam o servidor compilado por meio do cliente do SDK C# oficial, negociam capabilities, listam tools, inspecionam repositórios fixture reais e comparam o report retornado com um resultado direto do Engine.

## Tasks

1. Criar os projetos do host MCP e de testes e adicioná-los à solução.
2. Configurar o pacote estável do MCP C# SDK oficial, stdio, DI, metadata, logs em stderr e encerramento gracioso.
3. Implementar e testar a fronteira explícita de startup `--root`.
4. Implementar `inspect_repository`, saída estruturada, falhas sanitizadas, validação de caminhos e propagação de cancelamento.
5. Comprovar negociação do protocolo, discovery, execução de fixture, equivalência com o Engine, root inválido, diagnósticos, isolamento de stdout e ausência de regressão na CLI.
6. Adicionar as cinco projeções granulares read-only com schemas fechados de entrada/saída e resultados vazios/parciais determinísticos.
7. Exercitar cada tool do MVP por meio de um processo filho stdio real, incluindo entradas inválidas, projetos/SDKs ausentes, referências não resolvidas, cancelamento e shutdown.

## Implementation

O host usa `ModelContextProtocol` 2.2.0 e `Microsoft.Extensions.Hosting` 10.0.12 por meio do Central Package Management. O projeto MCP referencia Core e Engine, enquanto Core e Engine não possuem dependência de MCP. A suíte de protocolo usa o cliente do SDK oficial para iniciar o executável do servidor e não exige rede, credenciais ou LLM. Empacotamento e publicação continuam adiados para os itens de distribuição do roadmap.

Inicie o host ainda não empacotado a partir do output de build do repositório com um root explícito:

```text
dotnet DotNetRepoInspector.Mcp.dll --root /caminho/absoluto/do/repositorio
```

Argumentos inválidos de startup retornam exit code `2`, escrevem um erro sanitizado em stderr e não escrevem nada em stdout. Um processo válido executa até que seu token de cancelamento seja cancelado ou stdin alcance EOF.

## Contrato da tool

### `inspect_repository`

A tool é anunciada como read-only, não destrutiva, idempotente e closed-world. Todas as propriedades de entrada são opcionais porque o próprio repositório é fixado no startup:

```json
{
  "configurationPath": ".dotnetrepoinspector.json",
  "disableConfigurationFile": false,
  "excludedPaths": ["src/Legacy/Legacy.csproj"],
  "classificationOverrides": {
    "src/Web/Web.csproj": "web"
  }
}
```

`configurationPath`, cada item de `excludedPaths` e cada chave de `classificationOverrides` devem ser caminhos não vazios, relativos ao repositório e permanecer abaixo do root configurado. `configurationPath` e `disableConfigurationFile: true` são mutuamente exclusivos. Propriedades JSON desconhecidas são rejeitadas pelo schema de entrada gerado pelo SDK.

Um resultado bem-sucedido define `isError` do MCP como `false`. `data.report` é o `InspectionReport` existente, incluindo metadata do repositório, fatos do SDK configurado/resolvido, projetos, classificações avaliadas, referências e diagnósticos de repositório/projeto:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": {
      "schemaVersion": "1.3"
    }
  },
  "error": null
}
```

Uma falha esperada do adapter ou uma falha fatal do Engine define `isError` do MCP como `true`:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": false,
  "data": null,
  "error": {
    "code": "path_outside_repository_root",
    "message": "The supplied path resolves outside the configured repository root.",
    "details": {}
  }
}
```

Os códigos atuais de erro da tool são `invalid_tool_input`, `path_outside_repository_root` e `inspection_failed`. SDKs ausentes, projetos malformados, metadata Git indisponível e outras falhas recuperáveis de inspeção permanecem diagnósticos canônicos de `InspectionReport`. O cancelamento da requisição é nativo do protocolo: ele é propagado como cancelamento em vez de ser convertido em um envelope da tool.

### Tools granulares

Todas as tools granulares aceitam as mesmas propriedades opcionais de inspeção de `inspect_repository`, rejeitam propriedades desconhecidas e retornam `data.inspectionSchemaVersion`. `get_project_details` exige adicionalmente um `projectPath` normalizado e relativo ao repositório; um projeto ausente retorna `project_not_found`.

| Tool | Dados focados |
| --- | --- |
| `list_projects` | `projects` com path, name, target frameworks, classification e contagens de diagnósticos. |
| `get_project_details` | Um `ProjectInspection` canônico em `project`. |
| `get_project_reference_graph` | Paths de projetos, referências canônicas e diagnósticos de referências não resolvidas. |
| `get_repository_diagnostics` | Diagnósticos de repositório e projeto com contexto anulável em `projectPath`. |
| `get_sdk_metadata` | Fatos canônicos do SDK configurado e resolvido em `dotNetSdk`. |

Um repositório vazio retorna arrays vazios. Condições recuperáveis, como SDK ausente, projeto malformado ou referência não resolvida, permanecem resultados parciais bem-sucedidos com diagnósticos canônicos. Falhas fatais de inspeção usam `inspection_failed`; o cancelamento é propagado pelo protocolo sem envelope de tool.

## Referências

- [MCP C# SDK oficial](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK: transporte stdio](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)
- [MCP C# SDK: tools e conteúdo estruturado](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md)
- [ModelContextProtocol 2.2.0 no NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.2.0)
