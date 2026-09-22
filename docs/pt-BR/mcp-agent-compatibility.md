# Compatibilidade MCP com agentes e evals

**Idiomas:** [English](../en/mcp-agent-compatibility.md) | Português (Brasil)

Esta página registra a matriz de compatibilidade reproduzível e o plano de evals para o servidor local stdio `DotNetRepoInspector.Mcp`. Ela cobre as issues #133 e #139 do roadmap sem adicionar SDKs de providers nem dependências probabilísticas de LLM ao código de produto.

## Escopo e fontes

O servidor testado é o mesmo binário `DotNetRepoInspector.Mcp` documentado em [`architecture/mcp-server.md`](architecture/mcp-server.md). O alvo de compatibilidade é a fronteira MCP stdio: inicialização, discovery de tools e chamadas às tools read-only que projetam fatos a partir do `InspectionReport` canônico.

Documentação oficial dos clientes revisada em 2026-09-20:

- Configuração MCP do OpenAI Codex: <https://learn.chatgpt.com/docs/extend/mcp?surface=cli>
- Configuração MCP do Claude Code: <https://code.claude.com/docs/en/mcp>
- Configuração de MCP server no Gemini CLI: <https://github.com/google-gemini/gemini-cli/blob/main/docs/tools/mcp-server.md>

O servidor MCP usa `ModelContextProtocol` `2.2.0`. O runner determinístico de evals registra a versão de protocolo MCP `2025-06-18` nos relatórios.

## Specification

Requisitos de compatibilidade:

- todo cliente deve iniciar o mesmo executável do servidor com transporte stdio;
- todo cliente deve passar o repository root explicitamente por `--root`;
- o handshake deve publicar o nome `DotNetRepoInspector.Mcp`, versão do servidor, capabilities e as mesmas seis tools;
- o discovery deve incluir `inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics` e `get_sdk_metadata`;
- chamadas principais de tools devem retornar os mesmos fatos estruturados para a mesma fixture root;
- nenhum SDK de OpenAI, Anthropic, Google, Gemini ou outro provider entra em `DotNetRepoInspector.Mcp`;
- credenciais, transcripts e API keys específicos de cliente não devem ser commitados.

Requisitos dos evals:

- o dataset é versionado em `evals/DotNetRepoInspector.Mcp.Evals/Dataset/`;
- o ground truth deriva de fixtures e projeções de `InspectionReport`;
- os relatórios são emitidos em Markdown e JSON em `artifacts/mcp-evals/`;
- assertions determinísticas são preferidas sempre que existir verificação objetiva;
- LLM-as-judge não é fonte de verdade para fatos determinísticos;
- execuções probabilísticas com clientes reais permanecem opt-in e fora do gate padrão de CI.

## Plan

O runner determinístico usa o cliente oficial do MCP SDK como harness de protocolo. Ele não chama um modelo. Cada caso de eval inicia o mesmo servidor stdio para uma fixture root sintética, lista tools, chama a tool esperada e valida o conteúdo estruturado com assertions objetivas.

Validações live de clientes usam o mesmo binário e as mesmas fixture roots, mas cada cliente possui sua superfície de configuração:

- Codex: `codex mcp add <name> -- <command> --root <root>`
- Claude Code: `claude mcp add --transport stdio <name> -- <command> --root <root>`
- Gemini CLI: `settings.json` com `mcpServers.<name>.command` e `args`

Evals por provider/cliente devem comparar fatos estruturados em vez de exigir prosa final idêntica. Um cliente passa no smoke somente quando existe evidência de chamada real de tool e os fatos retornados batem com o ground truth determinístico.

## Tasks

O dataset versionado de eval cobre:

| Caso | Fixture | Tool esperada | Fatos objetivos |
| --- | --- | --- | --- |
| `tfm-project-index` | `ProjectKinds` | `list_projects` | target frameworks dos projetos Web e MultiTargeting |
| `classification-project-kinds` | `ProjectKinds` | `list_projects` | classificações Web, Worker, Console, Library e Test |
| `dependency-fan-out` | `ProjectReferences/FanOut` | `get_project_reference_graph` | A referencia B e C |
| `diagnostics-missing-sdk` | `Compatibility/MissingSdk` | `get_repository_diagnostics` | diagnóstico `DRI1002` |
| `diagnostics-unresolved-reference` | `ProjectReferences/Unresolved` | `get_project_reference_graph` | referência ausente e `DRI1003` |
| `graph-chain-interpretation` | `ProjectReferences/Chain` | `get_project_reference_graph` | A -> B -> C |
| `sdk-metadata-global-json` | `Sdk/WithGlobalJson` | `get_sdk_metadata` | SDK configurado `10.0.100` |

Métricas:

- **Task completion:** todas as assertions determinísticas de um caso passam.
- **Tool selection:** a sequência observada de tools é exatamente a tool esperada do caso.
- **Chamadas desnecessárias:** chamadas além da tool mínima esperada.
- **Fidelidade factual:** assertions factuais aprovadas divididas pelo total de assertions factuais.
- **Groundedness:** o runner determinístico retorna somente fatos vindos da saída estruturada da tool; execuções live devem citar ou preservar os fatos derivados da tool.
- **Afirmações não suportadas:** assertions determinísticas reprovadas ou afirmações live não suportadas pela saída da tool.

## Implementation

Compile o servidor e o runner de eval:

```bash
dotnet build src/DotNetRepoInspector.Mcp/DotNetRepoInspector.Mcp.csproj --configuration Release
dotnet build evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj --configuration Release
```

Execute os evals determinísticos no Windows:

```bash
dotnet run --project evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj \
  --configuration Release \
  -- \
  --server src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.exe \
  --fixtures tests/Fixtures \
  --output artifacts/mcp-evals \
  --client mcp-sdk-deterministic \
  --provider protocol \
  --model deterministic-assertions \
  --client-version 2.2.0
```

No Linux/macOS, use o caminho do executável sem extensão no mesmo diretório `bin/Release/net10.0/`.

Execução determinística histórica com binário de desenvolvimento (não o RC empacotado):

- timestamp UTC: `2026-09-20T08:25:59.7270206+00:00`
- OS/runtime: Windows `10.0.26200.0`, `.NET 10.0.12`, x64
- versão informada pelo binário de desenvolvimento: `DotNetRepoInspector.Mcp` `1.0.0` (não é a identidade do pacote `1.2.0-rc.1`)
- tools descobertas: todas as seis tools do MVP
- resultado: 7/7 casos concluídos
- task completion: 100%
- tool selection: 100%
- fidelidade factual: 100%
- groundedness: 100%
- chamadas desnecessárias: 0
- afirmações não suportadas: 0

As evidências do pacote RC estão registradas separadamente na [issue #137](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/137#issuecomment-5749538826): o pacote **`DotNetRepoInspector.Mcp` de versão exata `1.2.0-rc.1`**, resolvido via `dnx` de um **feed local controlado**, passou na validação de pacote/protocolo, nos **7/7 evals determinísticos** de fixtures e no **1/1 smoke de repositório real**. O [dry-run protegido de release](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/runs/35507871398) usou `publish=false`. Isso constitui evidência determinística do pacote RC, **não** da publicação no NuGet.org nem de um smoke do Codex contra o artefato RC.

## Matriz de Compatibilidade

| Cliente | Provider | Versão usada | Protocolo MCP | Configuração stdio | Configuração do root | Handshake | Discovery | Execução de tool | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OpenAI Codex CLI | OpenAI | `codex-cli 0.154.0-alpha.6.2` | `2025-06-18` via servidor `ModelContextProtocol` | `codex mcp add dri -- <server> --root <root>` | argumento explícito `--root` | validado por execução real do cliente | validado por `mcp_tool_call` para `list_projects` | validado: `list_projects` retornou 6 projetos | Smoke histórico com binário local aprovado; artefato RC exato não validado |
| Claude Code | Anthropic | não instalado neste ambiente | MCP stdio esperado | `claude mcp add --transport stdio dotnet-repo-inspector -- <server> --root <root>` | argumento explícito `--root` | pendente | pendente | pendente | Roteiro reproduzível documentado; não validado |
| Gemini CLI | Google | não instalado neste ambiente | MCP stdio esperado | `settings.json` com `mcpServers.dotnetRepoInspector.command` + `args` | argumento explícito `--root` | pendente | pendente | pendente | Roteiro reproduzível documentado; não validado |
| Harness determinístico MCP SDK | Harness de protocolo | `ModelContextProtocol` `2.2.0` | `2025-06-18` | `StdioClientTransport` | argumento explícito `--root` por fixture | validado | validado | validado em todas as categorias factuais do MVP | Aprovado no eval determinístico de protocolo |

Nota de release: o OpenAI Codex CLI foi validado com um executável local de desenvolvimento, mas a versão exata do artefato/pacote não foi registrada. Esse smoke histórico **não** comprova a validação do `1.2.0-rc.1`. O pacote exato `1.2.0-rc.1` passou nos evals determinísticos em feed local controlado, conforme registro acima; o smoke com Codex usando o **pacote RC exato publicado** permanece pendente até a publicação e deve ser registrado antes da promoção para GA. Claude Code e Gemini CLI não foram validados de forma independente; execuções com providers adicionais continuam como follow-ups não bloqueantes nas #133/#139.

## Smoke Tests Reproduzíveis

### Codex CLI

```bash
codex mcp add dri -- \
  ./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp \
  --root ./tests/Fixtures/ProjectKinds

codex mcp list
codex exec --ephemeral --json --sandbox read-only \
  "Use the MCP server named dri. Call list_projects. Return only JSON with keys tool_used and project_count."

codex mcp remove dri
```

Evidência esperada: JSONL contém um item `mcp_tool_call` com servidor `dri`, tool `list_projects`, `status` `completed` e conteúdo estruturado cujo comprimento de `data.projects` é 6.

Smoke histórico com Codex em 2026-09-20 (executável local de desenvolvimento; versão exata do pacote não registrada):

- o comando aceitou a configuração stdio;
- `codex mcp list` mostrou o servidor `dri` habilitado com comando stdio e `--root`;
- `codex exec --json` emitiu uma chamada real `mcp_tool_call` para `dri/list_projects`;
- a resposta final registrou `{"tool_used":"mcp__dri.list_projects","project_count":6}`;
- a configuração MCP global temporária foi removida após a validação.

Depois que o pacote `1.2.0-rc.1` for **publicado no NuGet.org**, repita esse smoke em um ambiente NuGet isolado para impedir a reutilização do pacote validado anteriormente no feed local controlado.

Use um diretório temporário e inicialmente vazio em `NUGET_PACKAGES` e um `NuGet.Config` temporário contendo somente:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Depois registre o servidor stdio no Codex com o pacote e a origem exatos, por exemplo:

```bash
export NUGET_PACKAGES="$(mktemp -d)"
codex mcp add dri -- \
  dnx DotNetRepoInspector.Mcp@1.2.0-rc.1 \
  --configfile /caminho/absoluto/NuGet.Config \
  --source https://api.nuget.org/v3/index.json \
  --no-http-cache \
  --yes -- \
  --root <caminho-absoluto-da-fixture>
```

O workflow protegido de release aplica o mesmo isolamento em `.github/scripts/invoke_mcp_package_smoke.ps1`: a origem configurada é exclusiva, `NUGET_PACKAGES` é recriado vazio a cada tentativa e o cache HTTP é desabilitado. O script também grava `package-source-evidence.json` com PackageId, versão exata, source, caminho do cache e modo de isolamento. Registre essa evidência junto com o evento `mcp_tool_call` e o resultado factual. Esse **smoke do Codex pós-publicação do RC está pendente**; ele não integra o resultado histórico acima.

### Claude Code

```bash
claude mcp add --transport stdio dotnet-repo-inspector -- \
  ./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp \
  --root ./tests/Fixtures/ProjectKinds

claude mcp list
```

No Claude Code, execute `/mcp` e pergunte:

```text
Use dotnet-repo-inspector list_projects and report the project count and classifications.
```

Fatos esperados: seis projetos, incluindo Web=`web`, Worker=`worker`, Console=`console`, Library=`library`, Test=`test`.

### Gemini CLI

Adicione ao `settings.json` apropriado do Gemini CLI:

```json
{
  "mcpServers": {
    "dotnetRepoInspector": {
      "command": "./src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp",
      "args": ["--root", "./tests/Fixtures/ProjectKinds"],
      "trust": false
    }
  }
}
```

Depois pergunte no Gemini CLI:

```text
Use the dotnetRepoInspector MCP server list_projects tool and report the project count and target frameworks.
```

Fatos esperados: seis projetos; `MultiTargeting/MultiTargeting.csproj` tem `net8.0` e `net10.0`.

## Limitações

- O runner determinístico prova compatibilidade de protocolo e assertions factuais, não comportamento real de LLM.
- Codex CLI foi o único cliente externo de provider instalado e autorizado no ambiente de validação.
- Os smokes de Claude Code e Gemini CLI permanecem pendentes até as CLIs serem instaladas e autenticadas quando necessário.
- Transcripts live devem ser reduzidos a evidência não sensível, como versão do cliente, evento de tool call, resumo do resultado estruturado e fatos pass/fail.
