# DotNetRepoInspector

**Idiomas:** [English](README.md) | Português (Brasil)

[![Build & Tests](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=rodri-oliveira-dev_DotNetRepoInspector&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=rodri-oliveira-dev_DotNetRepoInspector)
[![NuGet](https://img.shields.io/nuget/v/DotNetRepoInspector.svg)](https://www.nuget.org/packages/DotNetRepoInspector)
[![MCP NuGet](https://img.shields.io/nuget/v/DotNetRepoInspector.Mcp.svg?label=MCP%20NuGet)](https://www.nuget.org/packages/DotNetRepoInspector.Mcp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-%E2%89%A570%25-brightgreen)](.github/coverage-baseline.json)
[![Licença: MIT](https://img.shields.io/badge/Licen%C3%A7a-MIT-yellow.svg)](LICENSE)
[![GitHub Marketplace](https://img.shields.io/badge/GitHub%20Marketplace-DotNetRepoInspector-181717?logo=github)](https://github.com/marketplace/actions/dotnetrepoinspector)

**Inspecione e classifique projetos .NET usando metadados MSBuild avaliados para CI/CD, automação, governança de arquitetura e evidências históricas opcionais.**

> Status: **contrato v1 estável**. O contrato público da v1 está definido e validado em CI. Os artefatos oficiais são publicados somente pelo workflow protegido de Release.

## O que a v1 faz

O DotNetRepoInspector produz uma visão determinística e legível por máquina de um repositório .NET sem exigir análise de código-fonte ou banco de dados externo.

A superfície da v1 inclui:

- descoberta de projetos .NET SDK-style;
- fatos MSBuild avaliados como SDKs, target frameworks, output type, metadados de teste, packability, runtime identifiers e arestas de `ProjectReference`;
- metadados de `global.json` e SDK resolvido;
- metadados Git de repositório, commit, branch, remote e dirty state quando disponíveis;
- classificação base determinística: Web, Worker, Console, Library, Test e Unknown;
- JSON de inspeção versionado (`schemaVersion 1.3`);
- configuração opcional do repositório para exclusões e overrides explícitos de classificação;
- CLI/.NET Tool e Composite GitHub Action reutilizável;
- persistência HTTP/webhook opcional de snapshots com proveniência e idempotência;
- diagnósticos estruturados, cancelamento, compatibilidade cross-platform, hardening de segurança, guardrails de performance e validação contra repositórios públicos fixados.

Subtipos de aplicações e a camada opcional de políticas são trabalho pós-v1 e não fazem parte da promessa de compatibilidade da v1.

## Princípios de design

- **MSBuild é a fonte da verdade.** Propriedades efetivamente avaliadas têm precedência sobre heurísticas baseadas no XML bruto do projeto.
- **Zero configuração por padrão.** Uma inspeção útil exige apenas o caminho do repositório.
- **Automação em primeiro lugar.** A saída é determinística, legível por máquina e adequada para CI/CD.
- **Sem coleta de código-fonte.** O Inspector foca metadados de projeto e repositório.
- **Persistência é opcional.** A inspeção funciona sem banco, endpoint HTTP ou conta cloud.
- **Agnóstico de provedor.** GitHub Actions é uma integração de delivery, não a arquitetura central.
- **Contratos públicos versionados.** Regras de compatibilidade de produto, Action, CLI e JSON são documentadas e protegidas pelo release gate.

## Contrato JSON

O contrato v1 usa atualmente o schema de inspeção **1.3**. Um payload representativo é:

```json
{
  "schemaVersion": "1.3",
  "repository": {
    "name": "sample-service",
    "commitSha": "0123456789abcdef0123456789abcdef01234567",
    "branch": "main",
    "remoteUrl": "https://github.com/example/sample-service.git",
    "isDirty": false
  },
  "dotNetSdk": {
    "globalJsonPath": "global.json",
    "configured": {
      "version": "10.0.100",
      "rollForward": "latestFeature",
      "allowPrerelease": false
    },
    "resolvedVersion": "10.0.100"
  },
  "projects": [
    {
      "path": "src/App/App.csproj",
      "name": "App",
      "resolvedSdkVersion": "10.0.100",
      "sdks": [
        { "name": "Microsoft.NET.Sdk.Web" }
      ],
      "targetFrameworks": ["net10.0"],
      "outputType": "Exe",
      "isTestProject": false,
      "isPackable": false,
      "runtimeIdentifiers": [],
      "classification": {
        "kind": "web",
        "confidence": "high",
        "signals": ["sdk:Microsoft.NET.Sdk.Web"]
      },
      "references": [],
      "diagnostics": []
    }
  ],
  "diagnostics": []
}
```

O exemplo canônico e o contrato completo ficam em [`docs/pt-BR/schema/`](docs/pt-BR/schema/). Mudanças aditivas do schema permanecem na major `1`; uma quebra de contrato exige nova major de schema e produto e não pode mover o alias `v1` da Action.

## Instalar como .NET Tool

Package ID: `DotNetRepoInspector`  
Comando da Tool: `dotnet-repo-inspect`  
Invocação pública suportada: `dotnet repo-inspect`

O pacote tem como alvo .NET 10 e requer runtime/SDK .NET compatível para execução.

O pacote da CLI é publicado no NuGet.org. A instalação sem `--version` seleciona o pacote estável mais recente disponível nas fontes NuGet configuradas:

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --version
dotnet repo-inspect .
```

Também é possível instalar a Tool em um manifest local:

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

Para automação reproduzível, fixe uma versão explícita no seu próprio manifest/pipeline em vez de depender de uma versão de exemplo neste README. Contribuidores podem gerar e instalar um pacote local ainda não publicado. Consulte [`docs/pt-BR/cli.md`](docs/pt-BR/cli.md).

## Uso da CLI

Inspecione o repositório atual e emita JSON em stdout:

```bash
dotnet repo-inspect .
```

Grave o relatório em arquivo:

```bash
dotnet repo-inspect . --output artifacts/inspection.json
```

Use exclusões/overrides opcionais:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --classify src/App/App.csproj=web
```

O arquivo padrão `.dotnetrepoinspector.json` é opcional. Consulte [`docs/pt-BR/configuration.md`](docs/pt-BR/configuration.md) para formato versionado e regras de precedência.

A CLI mantém dados de máquina em stdout/arquivos de saída e logs operacionais em stderr. Os códigos de saída documentados distinguem erros no relatório, argumentos inválidos, falha fatal de inspeção, falha de escrita, falha fatal de persistência e cancelamento. Consulte [`docs/pt-BR/cli.md`](docs/pt-BR/cli.md).

## Servidor MCP

`DotNetRepoInspector.Mcp` expõe os mesmos fatos determinísticos da Engine para clientes MCP locais por meio de seis tools read-only sobre stdio. O servidor é um adapter de delivery adicional; a CLI, o JSON de inspeção e o propósito principal de inspeção de repositórios do produto permanecem inalterados.

O build de desenvolvimento atual é iniciado com uma fronteira explícita de repositório:

```bash
dotnet src/DotNetRepoInspector.Mcp/bin/Release/net10.0/DotNetRepoInspector.Mcp.dll \
  --root /caminho/absoluto/para/o/repositorio
```

O servidor não chama uma LLM nem inclui SDKs de providers. O OpenAI Codex CLI concluiu um smoke test com cliente real; as configurações de Claude Code e Gemini CLI estão documentadas, mas permanecem não validadas no ambiente deste projeto.

`DotNetRepoInspector.Mcp` é empacotado como .NET Tool framework-dependent e `McpServer` NuGet, com comando `dotnet-repo-inspector-mcp` e `.mcp/server.json` embutido. A publicação oficial é feita pelo workflow protegido de Release e o pacote é distribuído pelo NuGet.org e GitHub Packages. Com .NET 10 ou superior, o pacote estável mais recente pode ser executado diretamente com `dnx`:

```bash
dnx DotNetRepoInspector.Mcp --yes -- --root /caminho/absoluto/para/o/repositorio
```

Para automação reproduzível, use a sintaxe opcional `@<versão>` na sua própria configuração. Validações adicionais com Claude Code e Gemini CLI seguem como evidência não bloqueante de interoperabilidade.

<!-- mcp-name: io.github.rodri-oliveira-dev/dotnet-repo-inspector-mcp -->

Consulte o [guia de uso do MCP](docs/pt-BR/mcp.md) para setup, configuração de clientes, schemas das tools, exemplos, limites de segurança e troubleshooting. A [matriz de compatibilidade de clientes](docs/pt-BR/mcp-agent-compatibility.md) registra as evidências e validações pendentes; o documento de [readiness do GA](docs/pt-BR/mcp-ga-readiness.md) é a fonte da verdade sobre o estado da publicação.

## GitHub Action

> **Disponível no GitHub Marketplace:** [DotNetRepoInspector](https://github.com/marketplace/actions/dotnetrepoinspector). Use `@v1` para acompanhar releases compatíveis da linha v1. Para máxima reprodutibilidade, fixe uma tag completa imutável ou commit SHA disponível na página de Releases.

O repositório contém uma Composite Action reutilizável que executa exatamente a versão da .NET Tool fixada pela revisão da Action:

```yaml
- name: Checkout
  uses: actions/checkout@v7

- name: Inspecionar repositório .NET
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
```

Os outputs incluem `report-path`, `schema-version`, `inspector-version` e `exit-code`. A Action não exige permissão de escrita nem token do GitHub para inspecionar um repositório que já tenha sido feito checkout.

O alias público `@v1` já está disponível para uso direto no GitHub Actions. Fixe uma tag completa imutável ou commit SHA quando for necessária reprodutibilidade exata. Consulte [`docs/pt-BR/github-action.md`](docs/pt-BR/github-action.md).

## Imagens de container

Releases estáveis oficiais publicam a mesma imagem multi-arquitetura no **GHCR** e no **Docker Hub**:

- `ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector`
- `docker.io/rodrigodotnet/dotnet-repo-inspector`

As imagens têm como alvo `linux/amd64` e `linux/arm64`, executam como non-root, incluem as famílias de SDK .NET suportadas necessárias para avaliação MSBuild e são publicadas com verificação de SBOM/provenance. Uma execução local/offline endurecida pode usar a tag estável móvel:

```bash
mkdir -p artifacts
docker run --rm \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector:latest \
  /repo --output /artifacts/inspection.json
```

Para deployments reproduzíveis, fixe um digest imutável da imagem na sua própria automação em vez de depender de `:latest`. A execução em container reduz a fronteira operacional, mas **não** transforma avaliação MSBuild em sandbox.

## Canais de distribuição

O workflow protegido de Release valida uma vez e publica por canais independentes:

- pacotes CLI e MCP no **NuGet.org**;
- pacotes CLI e MCP no **GitHub Packages**;
- Action reutilizável por **GitHub Marketplace / Git tags**;
- imagens multi-arquitetura no **GHCR** e **Docker Hub**.

A GitHub Release final só é criada depois que todos os canais obrigatórios de publicação e os gates de verificação pós-publicação concluem com sucesso.


## Persistência HTTP opcional de snapshots

A persistência permanece desabilitada até um sink ser selecionado. O sink HTTP/webhook built-in envia o `InspectionSnapshot` canônico para um endpoint do consumidor e inclui a chave de idempotência no header `Idempotency-Key`.

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Credenciais Bearer são fornecidas somente via ambiente, nunca como argumento de CLI:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```

Persistência é `non-fatal` por padrão. No modo `fatal`, uma falha de entrega retorna exit code `5` depois que o relatório de inspeção já foi produzido. Consulte [`docs/pt-BR/persistence.md`](docs/pt-BR/persistence.md).

## Compatibilidade e fronteira de confiança

O Inspector tem como alvo .NET 10. O CI valida repositórios-alvo usando SDKs .NET 8 e .NET 10 lado a lado em Ubuntu, Windows e macOS.

A avaliação MSBuild **não é um sandbox**. Repositórios não confiáveis devem ser inspecionados somente em ambientes isolados, efêmeros, sem privilégios e sem credenciais ou dados sensíveis. Consulte [`SECURITY.md`](SECURITY.md) e [`docs/pt-BR/security.md`](docs/pt-BR/security.md).

## Readiness da release

A baseline de release da v1 está em formato legível por máquina em [`.github/release-readiness-v1.json`](.github/release-readiness-v1.json) e é protegida por testes do repositório. Ela mantém alinhados versão do produto, versão do schema, alias major da Action, metadados NuGet/.NET Tool, exemplo canônico do schema, o contrato publicado de distribuição por container e os arquivos obrigatórios de governança/segurança.

O checklist da primeira publicação, pré-requisitos externos de GitHub/NuGet, procedimento seguro de dry-run e verificação pós-publicação estão em [`docs/pt-BR/v1-release-readiness.md`](docs/pt-BR/v1-release-readiness.md). As regras gerais de SemVer, artifacts, tags, provenance e recuperação estão em [`docs/pt-BR/releases.md`](docs/pt-BR/releases.md).

Esta preparação do repositório não publica, por si só, pacote, tag ou GitHub Release. A publicação oficial é uma ação explícita do workflow protegido.

## Documentação

- [Documentação em Português (Brasil)](docs/pt-BR/README.md)
- [English documentation](docs/en/README.md)
- [Schema de inspeção v1](docs/pt-BR/schema/inspection-v1.md)
- [CLI / .NET Tool](docs/pt-BR/cli.md)
- [Servidor MCP](docs/pt-BR/mcp.md)
- [GitHub Action](docs/pt-BR/github-action.md)
- [Releases/versionamento](docs/pt-BR/releases.md)
- [Readiness da release v1](docs/pt-BR/v1-release-readiness.md)

## Arquitetura

```text
Repository
    |
    v
Inspection Engine ----> InspectionReport ----> JSON output
                           |
                           | optional
                           v
                 Snapshot Persistence
                           |
                           v
                    HTTP/webhook

Delivery hosts: CLI / .NET Tool, servidor MCP, GitHub Action e imagens de container
Pós-v1: sinks adicionais, policy/reporting, subtipos mais ricos
```

`DotNetRepoInspector.Core` contém os contratos normalizados e a classificação. A coleta de MSBuild e Git permanece em adapters. `DotNetRepoInspector.Persistence` contém contratos de snapshot/proveniência agnósticos de provider e `DotNetRepoInspector.Persistence.Http` é o primeiro sink concreto. Core e Engine continuam independentes de providers HTTP/banco e de credenciais.

## Contribuindo

Contribuições externas são suportadas. Comece por [`CONTRIBUTING.pt-BR.md`](CONTRIBUTING.pt-BR.md), siga o [`CODE_OF_CONDUCT.pt-BR.md`](CODE_OF_CONDUCT.pt-BR.md) e use [`SECURITY.md`](SECURITY.md) para reportar vulnerabilidades em vez de issues públicas.

Mudanças de classificação exigem fixtures sintéticas reproduzíveis e evidência avaliada; repositórios públicos podem revelar um bug, mas não substituem uma fixture local permanente de regressão.

## Roadmap

A fundação da v1 e os canais públicos de distribuição estão estabelecidos. A publicação oficial permanece uma operação protegida e explícita. O trabalho contínuo inclui subtipos mais ricos de aplicações, adapters adicionais de persistência quando justificados, uma camada opcional de políticas sobre o contrato normalizado e novas evidências de interoperabilidade.

## Licença

O DotNetRepoInspector é licenciado sob a [Licença MIT](LICENSE).
