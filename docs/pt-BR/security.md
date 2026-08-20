# Segurança e privacidade

**Idiomas:** [English](../en/security.md) | Português (Brasil)

O DotNetRepoInspector foi projetado para produzir um snapshot de metadados técnicos, e não um inventário de código-fonte ou segredos. Este documento define o limite atual de coleta, o modelo de confiança do MSBuild, as permissões de delivery e os controles que mantêm dados operacionais fora do relatório normalizado.

## Dados coletados

O `InspectionReport` normalizado pode conter apenas o contrato público documentado em [`schema/inspection-v1.md`](schema/inspection-v1.md):

- identidade do repositório: nome, SHA do commit de `HEAD`, branch simbólica, URL sanitizada de `origin` e estado dirty/clean quando disponível;
- metadados do SDK .NET: caminho do `global.json` aplicável, versão configurada, roll-forward, prerelease e versão resolvida;
- metadados dos projetos: caminho relativo e nome do projeto, SDK resolvido/declarado, target frameworks, output type, flags de teste/packable, runtime identifiers, classificação e caminhos normalizados de `ProjectReference`;
- diagnósticos estáveis: código `DRIxxxx`, severidade, mensagem estável, source normalizado e contexto controlado.

Os caminhos no relatório são normalizados e relativos à raiz do repositório quando o contrato assim define. Caminhos absolutos do workspace específicos da máquina não são expostos intencionalmente.

O adapter Git remove informações de usuário, query string e fragment de uma URL absoluta de remote antes que ela entre no contrato público. Isso evita serializar formatos comuns de remote contendo tokens.

## Dados não coletados ou serializados

O Inspector **não** coloca intencionalmente os seguintes dados no snapshot normalizado:

- conteúdo de código-fonte;
- conteúdo arbitrário de arquivos;
- dicionário bruto de propriedades do MSBuild;
- valores de variáveis de ambiente do processo;
- credenciais, senhas, API keys, access tokens, private keys ou connection strings;
- stdout/stderr bruto de processos filhos;
- material de autenticação do NuGet;
- tokens do GitHub ou secrets de workflow;
- conteúdo de arquivos de configuração além do pequeno conjunto de valores representado explicitamente pelo contrato público.

O `global.json` é lido somente para obter os campos de configuração de SDK suportados. Arquivos de projeto e imports são avaliados pelo MSBuild para obter os metadados efetivos, mas seu texto não é copiado para o relatório.

O contexto de diagnósticos recebe sanitização de defesa em profundidade durante a serialização: valores cujas chaves indiquem credenciais, como `token`, `password`, `connectionString`, `secret` ou `apiKey`, são emitidos como `<redacted>`.

## Sem upload automático

A CLI e a .NET Tool gravam o JSON da inspeção em stdout ou no arquivo local solicitado. A GitHub Action grava o relatório em um arquivo local do runner e expõe o caminho como output da Action. O DotNetRepoInspector não envia o relatório para um serviço remoto por conta própria.

Uma futura integração de persistência/sink deverá tornar a transferência de rede explícita e preservar as regras da seção **Credenciais de sinks** abaixo.

## Modelo de confiança do MSBuild

A avaliação do MSBuild **não é um sandbox nem uma fronteira de segurança**. A Microsoft documenta que lógica MSBuild desconhecida deve ser tratada como capaz de executar código no ambiente de build. Mesmo quando nenhum target é solicitado, a fase de evaluation pode processar imports, resolução de SDK, condições, propriedades originadas do ambiente e property functions. Property functions podem ler variáveis de ambiente e arquivos acessíveis.

O DotNetRepoInspector usa deliberadamente `-getProperty` / `-getItem` sem solicitar targets; portanto, targets e tasks normais de build não são executados apenas para coletar metadados. Isso reduz efeitos colaterais, mas não torna segura a avaliação de código não confiável.

As mitigações atuais de runtime são:

- MSBuild executa fora do processo do Inspector;
- argumentos usam `ProcessStartInfo.ArgumentList`; nenhum comando de shell é construído;
- nenhum target de build/design-time é solicitado para a coleta de metadados;
- cancelamento encerra a árvore do processo filho;
- `MSBUILDDISABLENODEREUSE=1` impede reutilização de workers do MSBuild entre inspeções;
- telemetria é desabilitada para processos `dotnet` criados pelo Inspector;
- variáveis de ambiente com nomes que indicam credenciais são removidas antes de iniciar `dotnet` e MSBuild;
- handles e ponteiros conhecidos para material de credenciais, como `SSH_AUTH_SOCK`, `GPG_AGENT_INFO`, `DOCKER_CONFIG` e `KUBECONFIG`, são removidos desses ambientes filhos;
- detalhes brutos de stdout/stderr do MSBuild não são mapeados para o `InspectionReport` normalizado.

A filtragem de ambiente é defesa em profundidade, não DLP. Um segredo armazenado em uma variável com nome incomum que não pareça sensível ainda pode ficar visível para o MSBuild. A evaluation também pode acessar arquivos e recursos de rede disponíveis para a identidade do sistema operacional. Atualmente não existe sandbox de filesystem ou rede.

## Inspeção de repositórios não confiáveis

Para código que você não confia completamente, use uma fronteira de segurança separada ao redor do DotNetRepoInspector:

- execute em runner/container/VM efêmero e descartável;
- não exponha credenciais de repositório, cloud, package feed, assinatura, SSH ou deployment ao job;
- evite containers privilegiados, sockets do host, mounts graváveis e acesso à rede de produção;
- restrinja saída de rede e acesso a metadata services de cloud quando o ambiente permitir;
- não coloque repositórios ou arquivos sensíveis em locais legíveis pela identidade de inspeção;
- pré-provisione apenas os SDKs/dependências necessários para a inspeção;
- destrua o ambiente após a execução.

Se um SDK privado ou extensão de build exigir credenciais durante a evaluation, prefira uma identidade dedicada, de curta duração e limitada somente ao package source necessário. Não reutilize credenciais de deployment ou produção. Observe que a filtragem dos processos filhos remove intencionalmente variáveis de ambiente comuns que pareçam conter credenciais; pré-provisionar dependências é mais seguro do que tornar secrets visíveis à evaluation do MSBuild.

## Permissões da GitHub Action

A Composite Action reutilizável não precisa de acesso de escrita à API do GitHub e não expõe input de token. Os próprios workflows de validação do repositório usam:

```yaml
permissions:
  contents: read
```

Workflows consumidores devem fazer o mesmo, a menos que outra etapa realmente exija permissões adicionais. Conceda permissões extras na fronteira mais restrita possível de job/step, em vez de ampliar o job de inspeção inteiro.

Ao fazer checkout do código para inspeção, recomenda-se `persist-credentials: false` quando etapas posteriores não precisarem de credenciais Git:

```yaml
permissions:
  contents: read

steps:
  - uses: actions/checkout@v7
    with:
      persist-credentials: false

  - uses: rodri-oliveira-dev/DotNetRepoInspector@v1
    with:
      path: .
```

Um workflow nunca deve inspecionar código não confiável em um job privilegiado contendo secrets apenas porque a própria Action solicita somente leitura.

## Logs e diagnósticos

Logs operacionais vão para stderr; JSON vai para stdout ou para o arquivo selecionado. A CLI:

- não registra valores brutos dos argumentos da linha de comando;
- registra tipos de exceção, e não mensagens brutas de exceção, na fronteira de delivery;
- faz redaction de contexto estruturado quando as chaves parecem sensíveis;
- mantém logs debug/verbose separados do JSON.

Diagnósticos de inspeção usam mensagens estáveis e controladas e contexto legível por máquina. Texto bruto específico de infraestrutura não é necessário no contrato público de diagnósticos.

Não adicione stdout/stderr bruto, dumps de ambiente, headers HTTP de autorização, connection strings, tokens ou mensagens de exceção originadas de SDKs/clients com credenciais a `details` de diagnóstico ou mensagens de log.

## Credenciais de sinks

Persistência/sinks são um recurso separado, mas qualquer sink futuro deve seguir estas regras:

- obter credenciais do secret store/ambiente do host ou workload identity, nunca do relatório de inspeção;
- nunca serializar credenciais de sink em `.dotnetrepoinspector.json` ou `InspectionReport`;
- não passar credenciais por argumentos de CLI ou URLs/query strings;
- preferir workload identity/OIDC e credenciais de curta duração a tokens estáticos de longa duração;
- usar escopos de menor privilégio limitados ao sink e operação necessários;
- usar TLS no transporte remoto e validar o destino;
- remover material de autenticação de logs estruturados e exceções;
- manter payloads de retry/dead-letter limitados ao snapshot de inspeção, sem contexto de credenciais do transporte.

## Reporte de vulnerabilidades

Consulte [`SECURITY.md`](../../SECURITY.md) na raiz do repositório para o processo privado de reporte. Não publique vulnerabilidades exploráveis ou credenciais reais em issues públicas.

## Referências

- Microsoft Learn — Secure MSBuild usage best practices: https://learn.microsoft.com/visualstudio/msbuild/msbuild-security-best-practices
- Microsoft Learn — Evaluate MSBuild items and properties: https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties
- Microsoft Learn — Property functions: https://learn.microsoft.com/visualstudio/msbuild/property-functions
- Microsoft Learn — Environment variables in MSBuild: https://learn.microsoft.com/visualstudio/msbuild/how-to-use-environment-variables-in-a-build
- GitHub Docs — Use `GITHUB_TOKEN` for authentication: https://docs.github.com/actions/security-for-github-actions/security-guides/automatic-token-authentication
