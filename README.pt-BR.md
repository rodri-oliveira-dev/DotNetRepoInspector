# DotNetRepoInspector

**Idiomas:** [English](README.md) | Português (Brasil)

[![CI](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml/badge.svg)](https://github.com/rodri-oliveira-dev/DotNetRepoInspector/actions/workflows/validate.yml)

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

## Uso planejado

### CLI

```bash
dotnet repo-inspect .
```

### GitHub Actions

```yaml
- name: Inspect .NET repository
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
```

A integração com GitHub Actions está planejada; este exemplo documenta a experiência pretendida para o consumidor, e não uma release já publicada.

## Documentação

A documentação é organizada por idioma, e cada árvore de idioma aponta apenas para arquivos do próprio idioma:

- [Documentação em Português (Brasil)](docs/pt-BR/README.md)
- [English documentation](docs/en/README.md)

## Arquitetura

```text
Repository
    |
    v
DotNetRepoInspector.MSBuild
    |
    v
DotNetRepoInspector.Core
    |
    +------------------+
    |                  |
    v                  v
CLI / JSON        Future integrations
                  (GitHub Action, sinks,
                   policy/reporting)
```

O Core contém os modelos normalizados de inspeção e as regras de classificação. A descoberta e a avaliação específicas de MSBuild permanecem atrás de um adapter. Consumidores como a CLI, GitHub Action e futuros sinks de persistência devem depender do modelo normalizado, em vez de duplicar a lógica de detecção do repositório.

## Estrutura do repositório

```text
.
├── .agents/skills/                    # Orientações específicas para agentes
├── .vscode/                           # Recomendações/configurações portáveis do VS Code
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
│   ├── DotNetRepoInspector.Core/      # Modelo de domínio, normalização e classificação
│   ├── DotNetRepoInspector.MSBuild/   # Descoberta de projetos e avaliação MSBuild
│   └── DotNetRepoInspector.Cli/       # CLI e fronteira de serialização
├── tests/
│   ├── DotNetRepoInspector.Core.Tests/
│   ├── DotNetRepoInspector.MSBuild.Tests/
│   ├── DotNetRepoInspector.Cli.Tests/
│   └── Fixtures/                      # Fixtures sintéticas de repositórios/projetos .NET
├── AGENTS.md
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

Os testes devem verificar o **comportamento avaliado**, e não suposições baseadas apenas em nomes de arquivo ou na estrutura bruta do XML.

## Persistência e evidências

A persistência intencionalmente não é obrigatória para o inspector. Uma futura abstração de sink poderá permitir que snapshots de inspeção sejam enviados para banco de dados, arquivo, object storage ou endpoint HTTP.

Um snapshot armazenado deve poder ser associado ao estado inspecionado do repositório, idealmente incluindo identidade do repositório, branch/ref, commit SHA, timestamp, versão do schema e versão do inspector. Isso torna evidências históricas de arquitetura reproduzíveis sem acoplar o Core a um banco de dados específico.

## Roadmap

- [ ] Estruturar a solution e os projetos
- [ ] Descobrir arquivos de projeto .NET suportados
- [ ] Avaliar propriedades efetivas do MSBuild
- [ ] Implementar classificação determinística de projetos
- [ ] Definir e versionar o contrato JSON
- [ ] Adicionar testes baseados em fixtures
- [ ] Empacotar a CLI como uma .NET tool
- [ ] Publicar uma GitHub Action reutilizável
- [ ] Adicionar sinks opcionais para snapshots
- [ ] Explorar verificações de políticas/compliance sobre resultados normalizados da inspeção

## Contribuindo

Contribuições, relatos de bugs e discussões de design são bem-vindos enquanto o projeto toma forma. Até que as diretrizes de contribuição sejam formalizadas, prefira alterações pequenas e focadas, acompanhadas de testes que demonstrem o comportamento inspecionado do repositório.
