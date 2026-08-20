# DotNetRepoInspector

**Idiomas:** [English](README.md) | Português (Brasil)

[![Build & Tests](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Coverage](https://img.shields.io/badge/coverage-%E2%89%A570%25-brightgreen)](.github/coverage-baseline.json)
[![Licença: MIT](https://img.shields.io/badge/Licen%C3%A7a-MIT-yellow.svg)](LICENSE)

**Inspecione e classifique projetos .NET, extraindo metadados de arquitetura para CI/CD, automação e governança técnica.**

> Status: desenvolvimento inicial. O repositório está sendo estruturado e os contratos públicos descritos abaixo ainda podem sofrer alterações.

## Por que DotNetRepoInspector?

Repositórios .NET frequentemente contêm uma combinação de aplicações Web, Workers, aplicações de console, bibliotecas, testes, múltiplos target frameworks, restrições de SDK e configurações de MSBuild no nível do repositório.

Plataformas de CI/CD e equipes de engenharia precisam redescobrir repetidamente essas informações por meio de scripts ad hoc. O DotNetRepoInspector busca fornecer uma visão única, normalizada e adequada à automação de um repositório, baseada nos metadados .NET/MSBuild efetivamente avaliados.

O objetivo de longo prazo é atender a três casos de uso relacionados:

1. **Inspecionar** — descobrir projetos .NET e os metadados efetivos de build.
2. **Classificar** — identificar papéis de projeto como Web, Worker, Console, Library, Test e Unknown.
3. **Rastrear** — opcionalmente persistir snapshots versionados das inspeções para que as equipes possam construir evidências técnicas e visões históricas.

## Princípios de design

- **MSBuild é a fonte da verdade.** Prefira propriedades avaliadas do projeto à leitura direta do XML do `.csproj`.
- **Zero configuração por padrão.** Uma inspeção útil deve exigir apenas o caminho do repositório.
- **Automação em primeiro lugar.** A saída deve ser determinística, legível por máquina e adequada a CI/CD.
- **Sem coleta de código-fonte.** A inspeção é focada nos metadados do projeto e do repositório, não no código-fonte da aplicação.
- **Persistência é opcional.** O inspector deve funcionar sem banco de dados ou serviço externo.
- **Agnóstico de provedor.** GitHub Actions é uma integração, não a arquitetura central.
- **Contratos versionados.** A saída legível por máquina deve carregar uma versão de schema conforme o projeto evolui.

## Escopo inicial

A primeira versão utilizável deve descobrir e expor:

- caminho e nome do projeto;
- SDK do projeto;
- tipo/classificação do projeto;
- `TargetFramework` / `TargetFrameworks`;
- `OutputType`;
- metadados de projeto de teste;
- metadados de empacotamento;
- runtime identifiers quando configurados;
- configuração de SDK do `global.json`;
- versão resolvida do SDK .NET;
- referências entre projetos;
- metadados do repositório e do commit quando disponíveis.

Classificações iniciais:

- Web
- Worker
- Console
- Library
- Test
- Unknown

Subtipos adicionais como Web API, Razor Pages, Blazor, Azure Functions e outros workloads poderão ser adicionados quando puderem ser identificados de forma confiável sem depender de convenções frágeis de nomes de arquivo.

## Exemplo de saída

O schema exato ainda não é definitivo, mas o formato pretendido é semelhante a:

```json
{
  "schemaVersion": "1.0",
  "repository": {
    "name": "example/repository",
    "commit": "61f842a"
  },
  "dotnet": {
    "configuredSdk": "10.0.100",
    "resolvedSdk": "10.0.4xx"
  },
  "projects": [
    {
      "name": "Orders.Api",
      "path": "src/Orders.Api/Orders.Api.csproj",
      "type": "web",
      "sdk": "Microsoft.NET.Sdk.Web",
      "targetFrameworks": ["net10.0"],
      "isTestProject": false,
      "isPackable": false
    }
  ]
}
```

## Instalar como .NET Tool

A CLI é empacotada com o package ID NuGet `DotNetRepoInspector` e o comando da tool `dotnet-repo-inspect`. Como o comando começa com `dotnet-`, a invocação pública é:

```bash
dotnet repo-inspect .
```

O pacote tem como alvo .NET 10. É necessário um runtime/SDK .NET compatível na máquina onde a tool será executada.

> O empacotamento e a instalação são validados no CI, mas o pacote ainda não foi publicado no NuGet.org. Os comandos abaixo que usam o feed público passam a se aplicar quando houver uma release publicada.

### Instalação global

```bash
dotnet tool install --global DotNetRepoInspector
dotnet repo-inspect --help
```

Atualize posteriormente com:

```bash
dotnet tool update --global DotNetRepoInspector
```

### Tool manifest local

```bash
dotnet new tool-manifest
dotnet tool install DotNetRepoInspector
dotnet repo-inspect .
```

Se o repositório já possuir um tool manifest, não execute `dotnet new tool-manifest` novamente. Restaure as tools fixadas com `dotnet tool restore` e atualize esta tool com `dotnet tool update DotNetRepoInspector`.

### Gerar e instalar um pacote local

Contribuidores podem validar o pacote distribuível sem publicar nada:

```bash
dotnet pack ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
  --configuration Release \
  --output ./artifacts/packages \
  -p:Version=0.0.0-local
dotnet tool install --global DotNetRepoInspector \
  --version 0.0.0-local \
  --add-source ./artifacts/packages
dotnet repo-inspect --version
```

A versão pode, portanto, ser fornecida pela automação de CI/release por `-p:Version=...` sem editar o arquivo de projeto. Consulte [a documentação da CLI](docs/pt-BR/cli.md) para instalação local, comandos de atualização/desinstalação, comportamento da saída e códigos de saída.

## Uso

Inspecione o repositório atual e grave JSON em stdout:

```bash
dotnet repo-inspect .
```

Salve a inspeção em um arquivo:

```bash
dotnet repo-inspect . --output inspection.json
```

O nome direto do executável também funciona para uma tool instalada globalmente:

```bash
dotnet-repo-inspect .
```

### Persistência HTTP opcional de snapshots

A persistência de snapshots é opt-in. Sem `--sink`, o Inspector não contata endpoint de persistência.

Envie o snapshot canônico da inspeção para um endpoint HTTP/HTTPS fornecido pelo consumidor:

```bash
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

O sink HTTP envia um `POST` com o JSON canônico de `InspectionSnapshot` e a chave do snapshot no header `Idempotency-Key`. Retry é limitado a falhas transitórias de transporte/timeout e respostas HTTP `408`, `429`, `500`, `502`, `503` e `504`.

Autenticação Bearer é fornecida intencionalmente apenas pelo ambiente do processo, nunca por argumento da CLI:

```bash
export DOTNET_REPO_INSPECTOR_HTTP_TOKEN="<secret>"
dotnet repo-inspect . \
  --sink http \
  --sink-url https://evidence.example/api/snapshots \
  --sink-failure-mode fatal
```

`--sink-failure-mode non-fatal` é o padrão. Use `fatal` quando o delivery da evidência for obrigatório para a pipeline; nesse caso uma falha de persistência retorna código `5` depois que o relatório de inspeção já foi produzido.

Nunca coloque tokens do sink em `.dotnetrepoinspector.json`, na URL do endpoint, em scripts versionados ou no JSON de inspeção. Consulte [a documentação de persistência](docs/pt-BR/persistence.md) para detalhes de timeout, cancelamento, retry, idempotência e tratamento de secrets.

### GitHub Actions

O repositório inclui uma Composite Action reutilizável que executa a mesma .NET Tool e a mesma engine da CLI:

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

Os principais outputs são `report-path`, `schema-version`, `inspector-version` e `exit-code`. A Action preserva a semântica de códigos de saída da CLI e não exige token do GitHub nem permissão de escrita para inspecionar um checkout existente.

A persistência HTTP opcional pode ser habilitada por inputs da Action. Secrets ficam fora da lista de argumentos da CLI:

```yaml
- name: Inspecionar e persistir evidência
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    output: artifacts/inspection.json
    sink-url: https://evidence.example/api/snapshots
    sink-token: ${{ secrets.INSPECTOR_EVIDENCE_TOKEN }}
    sink-failure-mode: fatal
```

> A implementação da Action é validada no CI, mas a tag pública `v1` e o pacote NuGet correspondente ainda não foram publicados. A publicação fica deliberadamente para o trabalho de automação de releases.

Consulte [a documentação da GitHub Action](docs/pt-BR/github-action.md) para inputs, outputs, requisitos de SDK, persistência, isolamento de package source, tratamento de falhas e exemplos de consumo posterior.

## Documentação

A documentação é organizada por idioma, e cada árvore de idioma aponta apenas para arquivos do próprio idioma:

- [Documentação em Português (Brasil)](docs/pt-BR/README.md)
- [English documentation](docs/en/README.md)

## Arquitetura

```text
Repository
    |
    v
Inspection Engine ----> InspectionReport ----> JSON output
                           |
                           | opcional
                           v
                 Snapshot Persistence
                           |
                           v
                    HTTP/webhook

Hosts de delivery: CLI / .NET Tool e GitHub Action
Adapters futuros: sinks adicionais, policy/reporting
```

O Core contém os modelos normalizados de inspeção e as regras de classificação. A descoberta e a avaliação específicas de MSBuild permanecem atrás de um adapter. `DotNetRepoInspector.Persistence` contém os contratos independentes de provedor para snapshot/proveniência, enquanto `DotNetRepoInspector.Persistence.Http` é o primeiro adapter concreto de delivery. Core e Engine permanecem independentes de HTTP, providers de banco e credenciais de sinks.

## Estrutura do repositório

```text
.
├── .agents/skills/                    # Orientações específicas para agentes
├── .github/action/                    # Glue de bootstrap/invocação da GitHub Action
├── .vscode/                           # Recomendações/configurações portáveis do VS Code
├── action.yml                         # Composite GitHub Action reutilizável
├── docs/
│   ├── en/                            # Documentação em inglês
│   │   ├── architecture/              # Documentação de arquitetura
│   │   ├── decisions/                 # Registros de decisões arquiteturais
│   │   └── schema/                    # Contrato JSON, documentação e exemplos
│   └── pt-BR/                         # Documentação em português (Brasil)
│       ├── architecture/
│       ├── decisions/
│       └── schema/
├── src/
│   ├── DotNetRepoInspector.Core/              # Modelo de domínio, normalização e classificação
│   ├── DotNetRepoInspector.Engine/            # Orquestração ponta a ponta da inspeção
│   ├── DotNetRepoInspector.Git/               # Adapter de metadados Git do repositório
│   ├── DotNetRepoInspector.MSBuild/           # Descoberta de projetos e avaliação MSBuild
│   ├── DotNetRepoInspector.Persistence/       # Snapshot, proveniência e abstrações de sink
│   ├── DotNetRepoInspector.Persistence.Http/  # Sink HTTP/webhook built-in
│   └── DotNetRepoInspector.Cli/               # CLI, serialização e composição de delivery
├── tests/
│   ├── DotNetRepoInspector.Core.Tests/
│   ├── DotNetRepoInspector.Engine.Tests/
│   ├── DotNetRepoInspector.Git.Tests/
│   ├── DotNetRepoInspector.MSBuild.Tests/
│   ├── DotNetRepoInspector.Persistence.Tests/
│   ├── DotNetRepoInspector.Persistence.Http.Tests/
│   ├── DotNetRepoInspector.Cli.Tests/
│   └── Fixtures/                              # Fixtures sintéticas de repositórios/projetos .NET
├── AGENTS.md
├── LICENSE
├── README.md
├── README.pt-BR.md
├── Directory.Build.props
├── Directory.Packages.props
└── global.json
```

## Estratégia de testes

O engine de inspeção deve ser validado principalmente com repositórios sintéticos de fixtures cobrindo combinações como:

- `Microsoft.NET.Sdk.Web`;
- `Microsoft.NET.Sdk.Worker`;
- tipos de saída executável e biblioteca;
- projetos de teste;
- herança de `Directory.Build.props`;
- projetos multi-target;
- propriedades condicionais de MSBuild;
- referências entre projetos;
- repositórios com e sem `global.json`.

Os testes devem verificar o **comportamento avaliado**, e não suposições baseadas apenas em nomes de arquivo ou na estrutura bruta do XML. Testes do adapter de persistência usam implementações de `HttpMessageHandler` em memória e não dependem de infraestrutura pública.

## Persistência e evidências

A persistência é opcional e acontece somente depois que existe um `InspectionReport` utilizável. `InspectionSnapshotFactory` cria um envelope atribuível contendo identidade de repositório/commit quando disponível, instante UTC da observação, versão do schema e do Inspector, digest do relatório e uma chave de idempotência versionada.

O publisher genérico aplica política de timeout/falha, mas não conhece HTTP ou bancos. O adapter HTTP built-in é selecionado explicitamente pelo host de delivery e pode enviar o snapshot para um endpoint fornecido pelo consumidor sem acoplar Core ou Engine a um provider de infraestrutura.

Uma falha de persistência não se torna diagnóstico de inspeção. O consumidor pode escolher delivery `non-fatal`, que preserva a semântica normal de saída da inspeção, ou `fatal`, que retorna código `5` mantendo inalterado o relatório já produzido.

## Roadmap

- [ ] Estruturar a solution e os projetos
- [ ] Descobrir arquivos de projeto .NET suportados
- [ ] Avaliar propriedades efetivas do MSBuild
- [ ] Implementar classificação determinística de projetos
- [ ] Definir e versionar o contrato JSON
- [ ] Adicionar testes baseados em fixtures
- [x] Empacotar a CLI como uma .NET tool
- [x] Implementar uma GitHub Action reutilizável; release/tag pública ainda pendente
- [x] Adicionar o primeiro sink opcional de snapshots (HTTP/webhook)
- [ ] Explorar verificações de políticas/compliance sobre resultados normalizados da inspeção

## Licença

O DotNetRepoInspector é licenciado sob a [Licença MIT](LICENSE).

## Contribuindo

Contribuições, relatos de bugs e discussões de design são bem-vindos enquanto o projeto toma forma. Até que as diretrizes de contribuição sejam formalizadas, prefira alterações pequenas e focadas, acompanhadas de testes que demonstrem o comportamento inspecionado do repositório.
