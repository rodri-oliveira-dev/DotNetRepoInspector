# Guia do servidor MCP

**Idiomas:** [English](../en/mcp.md) | Português (Brasil)

`DotNetRepoInspector.Mcp` é um servidor local e read-only de Model Context Protocol para consultar os fatos determinísticos já produzidos pelo DotNetRepoInspector. Destina-se a pessoas desenvolvedoras, mantenedoras de repositórios, equipes de plataforma e integradores de clientes de agentes que precisam de fatos sobre inventário de projetos, SDK, diagnósticos e grafo de referências sem interpretar diretamente a saída da CLI.

Este guia documenta a implementação que existe atualmente no repositório. Não descreve HTTP remoto, escrita no repositório ou RAG porque essas capacidades não existem. O pacote NuGet está implementado e validado em feeds controlados, mas não está publicado.

## Specification

O contrato da documentação é:

- preservar a identidade do DotNetRepoInspector como inspetor determinístico de repositórios .NET;
- explicar build local e inicialização stdio com `--root` explícito;
- descrever cada tool implementada a partir de seu contrato gerado e comportamento testado;
- distinguir testes de protocolo, clientes validados e configurações de clientes não validadas;
- explicitar a fronteira de confiança do MSBuild e o tratamento de dados pelo cliente;
- fornecer tarefas reproduzíveis de engenharia e diagnóstico de falhas sem exigir uma LLM no CI.

A aceitação se baseia na implementação MCP, nos testes de protocolo em nível de processo, nos testes de segurança, nas evidências de compatibilidade de clientes, no conteúdo sincronizado em inglês/PT-BR e em referências internas válidas.

## Arquitetura

```text
Cliente MCP
    |
    | MCP sobre stdio
    v
DotNetRepoInspector.Mcp
    |
    | IRepositoryInspector
    v
DotNetRepoInspector.Engine
    |                 |
    v                 v
Adapter MSBuild     Adapter Git
    |                 |
    +--------+--------+
             v
       InspectionReport
```

O processo MCP é responsável por parsing do startup, transporte stdio, schemas das tools, limites dos inputs, projeção de respostas, cancelamento e logging operacional sanitizado. A Engine permanece a fonte da verdade da inspeção e compõe fatos MSBuild avaliados e metadados Git no `InspectionReport` canônico. As tools granulares projetam visões menores desse report; elas não reimplementam descoberta, avaliação, classificação ou construção do grafo.

A direção das dependências é `Mcp -> Engine -> MSBuild/Git/Core`. Core, Engine, MSBuild e Git não dependem de MCP nem de provider de LLM. Consulte a [especificação da arquitetura MCP](architecture/mcp-server.md), a [ADR 0006](decisions/0006-mcp-adapter-architecture.md) e a [ADR 0008](decisions/0008-mcp-operational-reliability.md).

## Build e inicialização

Pré-requisitos:

- um SDK .NET 10 compatível com o `global.json` do repositório;
- os SDKs exigidos pelo repositório inspecionado;
- Git quando metadados Git forem esperados;
- um cliente MCP com suporte a servidores stdio locais.

Gere o servidor a partir deste repositório:

```bash
dotnet restore
dotnet build src/DotNetRepoInspector.Mcp/DotNetRepoInspector.Mcp.csproj --configuration Release --no-restore
```

Inicie diretamente o build framework-dependent:

```bash
dotnet src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.dll \
  --root /caminho/absoluto/para/o/repositorio
```

O executável no mesmo diretório de saída também pode ser usado como comando do cliente. No Windows ele é `DotNetRepoInspector.Mcp.exe`; no Linux/macOS não possui extensão.

O processo comunica somente mensagens MCP em stdout e grava logs operacionais estruturados em stderr. Uma inicialização pelo terminal parece ociosa enquanto aguarda input MCP; normalmente o cliente cria e controla esse processo filho. Fechar stdin encerra o processo de forma graciosa.

Argumentos de startup inválidos retornam exit code `2`. Exatamente um `--root <path>` ou `--root=<path>` é obrigatório, o diretório deve existir e argumentos de startup desconhecidos são rejeitados.

### Estado da distribuição e `dnx`

`DotNetRepoInspector.Mcp` é uma .NET Tool framework-dependent com package ID `DotNetRepoInspector.Mcp`, comando `dotnet-repo-inspector-mcp`, package types `DotnetTool` e `McpServer`, símbolos e `.mcp/server.json` embutido. O manifesto declara stdio e exige o filepath `--root`; não contém input nem valor de credencial.

O workflow protegido empacota e valida uma versão exata em lockstep com o produto. A partir de uma fonte local controlada:

```bash
dnx DotNetRepoInspector.Mcp@1.2.0 \
  --source /caminho/absoluto/para/pacotes \
  --yes -- \
  --root /caminho/absoluto/para/o/repositorio
```

O mesmo pacote pode ser instalado convencionalmente:

```bash
dotnet tool install --tool-path ./tools DotNetRepoInspector.Mcp \
  --version 1.2.0 --add-source /caminho/absoluto/para/pacotes
./tools/dotnet-repo-inspector-mcp --root /caminho/absoluto/para/o/repositorio
```

O pacote ainda não foi publicado no NuGet.org. O comando `dnx` público só se torna consumível depois que o workflow protegido de release publicar essa versão exata; não confunda dry-run local aprovado com publicação.

## Repository root e fronteira de filesystem

`--root` é fixado durante toda a vida do servidor e é o repositório inspecionado por cada chamada de tool. A configuração do cliente deve passá-lo como argumento separado do comando:

```text
command: dotnet
args: ["/caminho/absoluto/DotNetRepoInspector.Mcp.dll", "--root", "/caminho/absoluto/repositorio"]
```

Os caminhos das tools são relativos ao repositório. Caminhos absolutos, vazios, traversal para fora do root e caminhos que cruzam link/reparse point do filesystem são rejeitados. Os limites incluem 1.024 caracteres por caminho relativo, 256 exclusões, 256 overrides de classificação, arquivo de configuração de 1 MiB e resultado bem-sucedido de tool de 8 MiB.

Essa fronteira restringe caminhos fornecidos pelo cliente; ela não é um sandbox do sistema operacional. A avaliação MSBuild pode carregar props, targets, SDK resolvers, tasks e property functions controlados pelo repositório, e esses componentes podem executar código ou acessar recursos disponíveis ao processo. Inspecione repositórios não confiáveis somente em ambiente isolado, efêmero, sem privilégios, credenciais, secrets ou mounts sensíveis. Consulte o [threat model MCP](architecture/mcp-threat-model.md).

## Contratos comuns de input e resposta

Todas as seis tools são anunciadas como read-only, não destrutivas, idempotentes e closed-world. Propriedades de input desconhecidas são rejeitadas. Exceto pelo `projectPath` obrigatório de `get_project_details`, todo input é opcional:

| Propriedade | Tipo JSON | Significado |
| --- | --- | --- |
| `configurationPath` | `string` | Caminho relativo ao repositório para um arquivo de configuração do DotNetRepoInspector. |
| `disableConfigurationFile` | `boolean` | Desabilita tanto o arquivo de configuração padrão quanto um explícito. O padrão é `false`. |
| `excludedPaths` | `string[]` | Caminhos de projetos relativos ao repositório a omitir. |
| `classificationOverrides` | `object<string,string>` | Valores de classificação por caminho de projeto relativo ao repositório. |

`configurationPath` não pode ser combinado com `disableConfigurationFile: true`. Quando nenhum deles é fornecido, a Engine pode carregar `.dotnetrepoinspector.json` do root. A semântica de configuração está em [configuration.md](configuration.md).

Toda resposta estruturada usa este envelope:

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {},
  "error": null
}
```

Em uma falha esperada da tool, `ok` é `false`, `data` é `null`, o `isError` MCP é true e `error` contém `code`, uma `message` sanitizada e um objeto `details` vazio. Respostas granulares bem-sucedidas incluem `data.inspectionSchemaVersion`; a tool completa retorna essa versão em `data.report.schemaVersion`.

## Catálogo de tools

### `inspect_repository`

Objetivo: retornar o `InspectionReport` canônico completo, incluindo metadados do repositório/Git, dados do SDK configurado e resolvido, projetos, classificações, referências e diagnósticos.

Schema de input: os quatro [inputs comuns](#contratos-comuns-de-input-e-resposta); nenhuma propriedade obrigatória e `additionalProperties: false`.

Exemplo de request:

```json
{
  "excludedPaths": ["src/Legacy/Legacy.csproj"],
  "classificationOverrides": {
    "src/Web/Web.csproj": "web"
  }
}
```

Estrutura resumida da resposta (o report completo segue o [schema de inspeção](schema/inspection-v1.md)):

```json
{
  "mcpSchemaVersion": "1.0",
  "ok": true,
  "data": {
    "report": {
      "schemaVersion": "1.3",
      "repository": {},
      "dotNetSdk": {},
      "projects": [],
      "diagnostics": []
    }
  },
  "error": null
}
```

Erros esperados: todos os erros comuns listados abaixo. Problemas recuperáveis de projeto, SDK, configuração e Git normalmente aparecem como diagnósticos do report, não como erros MCP da tool.

### `list_projects`

Objetivo: retornar um índice compacto, ordenado por path, para inventário e roteamento de tools.

Schema de input: os quatro inputs comuns; nenhuma propriedade obrigatória e `additionalProperties: false`.

Exemplo de request: `{}`

Dados da resposta:

```json
{
  "inspectionSchemaVersion": "1.3",
  "projects": [
    {
      "path": "src/App/App.csproj",
      "name": "App",
      "targetFrameworks": ["net10.0"],
      "classification": {
        "kind": "web",
        "confidence": "high",
        "signals": ["sdk:Microsoft.NET.Sdk.Web"]
      },
      "diagnosticCount": 0,
      "errorCount": 0,
      "warningCount": 0
    }
  ]
}
```

Erros esperados: erros comuns das tools. Um repositório vazio tem sucesso com `projects: []`.

### `get_project_details`

Objetivo: retornar um `ProjectInspection` canônico selecionado pelo caminho relativo ao repositório obtido em `list_projects`.

Schema de input: `projectPath` é uma string obrigatória; os quatro inputs comuns são opcionais; `additionalProperties: false`.

Exemplo de request:

```json
{
  "projectPath": "src/App/App.csproj"
}
```

Dados resumidos da resposta (o `ProjectInspection` completo segue o [schema de inspeção](schema/inspection-v1.md)):

```json
{
  "inspectionSchemaVersion": "1.3",
  "project": {
    "path": "src/App/App.csproj",
    "name": "App",
    "targetFrameworks": ["net10.0"],
    "references": [],
    "diagnostics": []
  }
}
```

Erros esperados: erros comuns mais `project_not_found` quando o path normalizado não existe no resultado da inspeção.

### `get_project_reference_graph`

Objetivo: retornar cada path de projeto com suas arestas canônicas de `ProjectReference` e diagnósticos de referências não resolvidas.

Schema de input: os quatro inputs comuns; nenhuma propriedade obrigatória e `additionalProperties: false`.

Exemplo de request: `{}`

Dados da resposta:

```json
{
  "inspectionSchemaVersion": "1.3",
  "projects": [
    {
      "path": "src/App/App.csproj",
      "references": [
        { "path": "src/Library/Library.csproj" }
      ],
      "diagnostics": []
    }
  ]
}
```

Erros esperados: erros comuns das tools. Um alvo ausente permanece como aresta de referência e carrega o diagnóstico `DRI1003`; não é um erro fatal da tool. Consulte [project-reference-graph.md](project-reference-graph.md).

### `get_repository_diagnostics`

Objetivo: retornar diagnósticos do repositório e dos projetos em uma lista com contexto de projeto anulável.

Schema de input: os quatro inputs comuns; nenhuma propriedade obrigatória e `additionalProperties: false`.

Exemplo de request: `{ "disableConfigurationFile": true }`

Dados resumidos da resposta (os registros de diagnóstico também contêm os campos anuláveis `source`, `details` e `context`):

```json
{
  "inspectionSchemaVersion": "1.3",
  "diagnostics": [
    {
      "projectPath": "src/App/App.csproj",
      "diagnostic": {
        "code": "DRI1002",
        "severity": "error",
        "message": "The required .NET SDK could not be resolved."
      }
    }
  ]
}
```

Erros esperados: erros comuns das tools. Nenhuma ocorrência resulta em um array `diagnostics` vazio e bem-sucedido. Consulte o [catálogo de diagnósticos](diagnostics.md) para significados estáveis em vez de comparar o texto da mensagem.

### `get_sdk_metadata`

Objetivo: distinguir a configuração do `global.json` do repositório, o SDK resolvido por `dotnet` e diagnósticos de resolução do SDK.

Schema de input: os quatro inputs comuns; nenhuma propriedade obrigatória e `additionalProperties: false`.

Exemplo de request: `{}`

Dados da resposta:

```json
{
  "inspectionSchemaVersion": "1.3",
  "dotNetSdk": {
    "globalJsonPath": "global.json",
    "configured": {
      "version": "10.0.100",
      "rollForward": "latestFeature",
      "allowPrerelease": false
    },
    "resolvedVersion": "10.0.100"
  }
}
```

Erros esperados: erros comuns das tools. Um SDK solicitado indisponível normalmente é representado por `DRI1002` nos diagnósticos canônicos da inspeção e por fatos anuláveis/não resolvidos do SDK.

### Erros comuns das tools

| Código | Significado |
| --- | --- |
| `invalid_tool_input` | Um valor obrigatório está vazio, propriedades conflitam, um override é inválido ou uma propriedade desconhecida foi fornecida. |
| `path_outside_repository_root` | Um path é absoluto ou resolve para fora de `--root`. |
| `path_through_link` | Um path fornecido atravessa link/reparse point do filesystem. |
| `input_too_large` | Um path, coleção ou arquivo de configuração excede um limite do servidor. |
| `result_too_large` | Um resultado bem-sucedido serializado excederia 8 MiB UTF-8. |
| `server_busy` | Uma inspeção está em execução e as oito posições da fila estão ocupadas. Tente novamente depois. |
| `inspection_timed_out` | Espera na fila mais inspeção excedeu cinco minutos. |
| `inspection_failed` | A Engine falhou antes de produzir um `InspectionReport`. |
| `project_not_found` | `get_project_details` não encontrou o projeto solicitado no resultado. |

O cancelamento pelo cliente é propagado pelo MCP e não se torna um envelope de erro.

## Configuração de clientes

Use paths absolutos para comando, DLL e repositório em configurações persistentes de clientes. Substitua os placeholders por paths locais e execute o build antes.

### OpenAI Codex CLI: validado

O projeto validou o Codex CLI `0.154.0-alpha.6.2` com handshake stdio real, discovery de tools e chamada de `list_projects`:

```bash
codex mcp add dri -- dotnet /caminho/absoluto/DotNetRepoInspector.Mcp.dll \
  --root /caminho/absoluto/repositorio
codex mcp list
```

Peça ao Codex para usar `dri/list_projects` e remova o registro temporário quando apropriado:

```bash
codex mcp remove dri
```

### Claude Code: documentado, não validado

Este setup segue a documentação do cliente, mas não foi executado no ambiente de validação do projeto porque `claude` não estava instalado:

```bash
claude mcp add --transport stdio dotnet-repo-inspector -- \
  dotnet /caminho/absoluto/DotNetRepoInspector.Mcp.dll \
  --root /caminho/absoluto/repositorio
claude mcp list
```

Use `/mcp` no Claude Code para inspecionar o estado do servidor. Esta é uma rota reproduzível com validação pendente, não uma declaração de suporte.

### Gemini CLI: documentado, não validado

Este formato de `settings.json` segue a documentação do Gemini CLI, mas não foi executado no ambiente de validação do projeto porque `gemini` não estava instalado:

```json
{
  "mcpServers": {
    "dotnetRepoInspector": {
      "command": "dotnet",
      "args": [
        "/caminho/absoluto/DotNetRepoInspector.Mcp.dll",
        "--root",
        "/caminho/absoluto/repositorio"
      ],
      "trust": false
    }
  }
}
```

Somente o Codex possui evidência de cliente externo real neste momento. Os testes de processo com o SDK oficial comprovam o comportamento do protocolo MCP, mas não são evidência de um cliente LLM externo. Consulte a [matriz completa de compatibilidade e o procedimento de smoke](mcp-agent-compatibility.md).

## Exemplos de tarefas de engenharia

- Inventário do repositório: chame `list_projects`; compare classificações, target frameworks e contagens de diagnósticos sem obter o report completo.
- Planejamento de migração de framework: chame `list_projects`; identifique projetos cujos `targetFrameworks` não incluem o TFM alvo e use `get_project_details` para os projetos selecionados.
- Análise de impacto de dependências: chame `get_project_reference_graph`; percorra arestas de entrada/saída entre projetos e sinalize referências `DRI1003` não resolvidas.
- Diagnóstico do ambiente de build: chame `get_sdk_metadata` e depois `get_repository_diagnostics`; diferencie a solicitação do `global.json` do SDK resolvido e procure `DRI1002`.
- Triagem de CI: chame `get_repository_diagnostics`; agrupe ocorrências por `projectPath` anulável e código estável do diagnóstico.
- Snapshot auditável: chame `inspect_repository` quando o consumidor precisar do contrato versionado completo em vez de uma projeção focada.

A saída das tools contém fatos, não uma interpretação de LLM. Agentes devem citar paths/códigos retornados e evitar afirmações sem suporte.

## Segurança e tratamento de dados

O próprio servidor não usa SDKs de OpenAI, Anthropic, Google ou outra LLM, não escolhe modelo e não envia dados do repositório a um provider. Ele extrai metadados allow-listed de projeto/repositório por meio da Engine existente. Não coleta intencionalmente texto do código-fonte da aplicação, credenciais, valores de variáveis de ambiente, connection strings ou credenciais NuGet.

Um cliente MCP recebe os resultados das tools e pode enviar prompts, resultados ou contexto derivado a um modelo conforme configurações, conta, implantação, política de retenção e termos do provider desse cliente. Configure aprovações/trust e controles de dados do cliente antes de expor um repositório. O servidor não pode controlar como um cliente ou modelo trata os dados depois da entrega.

Annotations read-only do MCP significam que as tools não modificam intencionalmente o repositório. Elas não transformam o processo em sandbox e não neutralizam efeitos colaterais incorporados em lógica MSBuild não confiável. Variáveis de ambiente são filtradas para processos filhos e contexto sensível de diagnósticos é redigido, mas isolamento continua sendo o controle necessário para inputs não confiáveis.

## Troubleshooting

### Handshake ou discovery falha

- Gere o projeto MCP e aponte o cliente para a DLL ou executável da plataforma, não para o diretório do projeto.
- Verifique se o comando funciona com exatamente um `--root` existente; exit code `2` indica falha nos argumentos de startup.
- Confirme que o cliente está configurado para stdio, não HTTP/SSE.
- Verifique stderr/logs do cliente. Não adicione banners, prints de debug ou wrappers de shell que escrevam em stdout.

### O processo stdio parece travado

Isso é esperado quando iniciado manualmente: o servidor aguarda input JSON-RPC/MCP. Deixe o cliente MCP iniciá-lo. Garanta que nenhum wrapper aguarde input interativo e que nenhum processo filho herde stdin.

### `--root` é rejeitado

Use um único diretório existente. Prefira paths absolutos na configuração do cliente. Não coloque `--root` em opções exclusivas do cliente nem combine as duas formas de root. Coloque paths com espaços entre aspas conforme o formato de argumentos do cliente.

### Um path de tool é rejeitado

Use um path relativo ao repositório retornado por `list_projects`. Paths absolutos, escapes com `..`, strings vazias e traversal por link/reparse point são bloqueados deliberadamente. `configurationPath` também é relativo a `--root`.

### .NET está ausente ou o servidor não inicia

Instale um SDK/runtime .NET 10 compatível e verifique `dotnet --info`. Este build de desenvolvimento é framework-dependent. Um host ausente é uma falha de inicialização do processo pelo cliente, então nenhum handshake MCP ocorre.

### O SDK do repositório não é resolvido

Verifique `global.json`, SDKs instalados com `dotnet --list-sdks` e o [guia de compatibilidade](compatibility.md). Um servidor em execução normalmente informa um SDK de repositório indisponível como diagnóstico `DRI1002`; isso é diferente do próprio host .NET 10 estar indisponível.

### Contaminação de stdout ou erros de parsing JSON

stdout é exclusivamente o transporte MCP. Redirecione logs de wrappers/aplicação para stderr e evite `Write-Host`, `echo`, profiles de shell ou scripts de inicialização que emitam texto antes do servidor. O host built-in já grava logs em stderr.

### O cliente não consegue iniciar o servidor

Use paths absolutos, verifique suposições de working directory, coloque cada argumento entre aspas corretamente e execute o comando exato fora do cliente para examinar exit code/stderr. Em Unix, verifique permissões quando iniciar diretamente o apphost; usar `dotnet <dll>` evita essa exigência.

### `dnx` não encontra ou não executa o servidor

Use SDK .NET 10 ou posterior, fixe o pacote como `DotNetRepoInspector.Mcp@<versao-exata>` e coloque opções do `dnx` antes de `--`; argumentos depois de `--` são enviados ao servidor. Para pacote local/não publicado, passe `--source <diretorio-de-pacotes>`. Uma fonte NuGet.org funciona somente depois que essa versão exata for oficialmente publicada e indexada.

### Chamadas estão busy, expiram ou retornam dados demais

O servidor executa uma inspeção por vez, mantém oito na fila e aplica limite de cinco minutos. Tente novamente em `server_busy`, reduza o escopo do repositório com configuração/exclusões ou use tools granulares. `result_too_large` exige um resultado focado menor; não há truncamento automático.

## Referências

- [Arquitetura MCP e contrato exato](architecture/mcp-server.md)
- [Threat model MCP](architecture/mcp-threat-model.md)
- [Compatibilidade de clientes e evidências de evals](mcp-agent-compatibility.md)
- [Schema JSON de inspeção](schema/inspection-v1.md)
- [Diagnósticos](diagnostics.md)
- [Segurança](security.md)
