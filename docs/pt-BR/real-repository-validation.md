# Validação com repositórios reais

**Idiomas:** [English](../en/real-repository-validation.md) | Português (Brasil)

As fixtures sintéticas continuam sendo a suíte principal de regressão do DotNetRepoInspector porque são pequenas, determinísticas, revisáveis e independentes de serviços externos. A validação com repositórios reais complementa essas fixtures ao exercitar o Inspector contra repositórios .NET públicos e fixados por commit, contendo combinações e estilos de organização que não vale a pena reproduzir integralmente em fixtures locais.

## Separação da suíte padrão

A validação com repositórios reais é intencionalmente isolada do fluxo normal de build e testes.

- Os testes padrão da solução não clonam nem consultam repositórios externos.
- Depois do restore das dependências do Inspector, a suíte padrão continua executável sem acesso à Internet.
- A validação externa roda no workflow separado `Validate real repositories` ou pela execução explícita do script do repositório.
- Repositórios externos não são adicionados como dependências dos projetos de teste nem copiados para a árvore de fixtures.
- Uma falha causada apenas pela disponibilidade da rede/GitHub não altera a semântica da suíte sintética de regressão.

## Amostra versionada

O manifesto de validação fica em [`../../.github/real-repositories/manifest.json`](../../.github/real-repositories/manifest.json). Cada repositório é fixado por um SHA completo de commit e cada cenário possui expectativas explícitas.

| Repositório | Commit fixado | Raiz de inspeção | Principais cenários |
| --- | --- | --- | --- |
| `MassTransit/Sample-Outbox` | `1ab8e66ebf96e5733e68c2f4d2201276f38ed9c5` | raiz do repositório | Web, Worker, library, referências entre projetos, `net8.0` |
| `ardalis/CleanArchitecture` | `fbdc0951879f5e8dca1bebc273d4b28cb2934469` | `tests/Clean.Architecture.AspireTests` | classificação de teste, `Directory.Build.props` ancestral, `global.json` ancestral, `net9.0`, referência fora da raiz de inspeção |
| `App-vNext/Polly` | `47e3b412e8c3b7e6db1629acd98f3e3b6b529d6c` | `src/Polly.Core` | library multi-target, `Directory.Build.props` importado, seleção exata do SDK |

Alterar um desses commits é uma mudança revisável do baseline de compatibilidade. O manifesto nunca deve seguir implicitamente uma branch ou tag.

## Reprodutibilidade e segurança

O harness limita deliberadamente o que um repositório externo pode fazer o job de validação executar.

- URLs devem ser públicas no formato `https://github.com/<owner>/<repository>.git`.
- Commits devem ser SHAs Git completos de 40 caracteres hexadecimais minúsculos.
- Cada repositório é obtido no commit fixado e aberto com `HEAD` destacado.
- Submódulos não são inicializados.
- O manifesto não aceita comandos shell arbitrários nem etapas de preparação.
- O harness **não** executa restore, build, testes, scripts de pacote ou comandos específicos dos repositórios externos.
- Apenas o próprio DotNetRepoInspector é restaurado e compilado pelo workflow.
- O ambiente de validação instala os SDKs .NET necessários aos cenários atuais (`8.0.424`, `9.0.317` e `10.0.400`).
- Os relatórios carregam o commit Git observado, e o harness falha quando ele não corresponde ao commit fixado.

O DotNetRepoInspector continua invocando avaliação MSBuild como parte de sua fronteira normal de inspeção. O harness externo não adiciona outro mecanismo de execução de projetos além do comportamento já pertencente ao produto.

## Expectativas

O manifesto pode validar fatos estáveis e normalizados, como:

- código de saída da CLI;
- quantidade mínima de projetos descobertos;
- commit SHA do repositório;
- versão configurada e resolvida do SDK;
- caminho do projeto;
- classificação do projeto;
- `IsTestProject`;
- identidade do SDK do projeto;
- target frameworks;
- caminhos normalizados de `ProjectReference`.

As assertions ficam limitadas a fatos significativos para o comportamento público do Inspector. Caminhos absolutos específicos de máquina e saídas transitórias não entram no baseline.

## Execução local

A validação externa exige acesso à rede e as versões de SDK necessárias ao manifesto.

Compile primeiro o Inspector:

```bash
dotnet restore ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj
dotnet build ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj --configuration Release --no-restore
```

Depois execute o harness com PowerShell 7 ou superior:

```powershell
./.github/scripts/validate_real_repositories.ps1
```

O script grava um `inspection.json` e um `stderr.txt` por cenário, além do resumo consolidado em `artifacts/real-repositories/summary.md`.

Depois que o workflow estiver presente na branch padrão, `Validate real repositories` também poderá ser iniciado manualmente via `workflow_dispatch`. Pull requests que alterem o manifesto, o script do harness ou o próprio workflow executam automaticamente a validação externa.

## Política de reprodução de bugs

Um repositório real é uma fonte de descoberta de casos, não um substituto permanente para um teste de regressão isolado. Quando a validação externa revelar um defeito do Inspector:

1. Confirme a divergência contra o commit externo fixado.
2. Reduza o formato relevante do MSBuild/projeto para a menor fixture sintética local capaz de reproduzir o defeito.
3. Adicione um teste de regressão que falhe usando essa fixture local.
4. Corrija o Inspector com base nessa reprodução determinística.
5. Execute novamente o cenário do repositório real e atualize as expectativas somente quando o comportamento público pretendido tiver mudado.

Não corrija uma regra de produção com base apenas no nome, caminho, pacote ou layout incidental de um repositório externo específico.

## Limitações conhecidas

Esta validação deliberadamente não comprova todo formato possível de repositório .NET.

- Arquivos `.props`/`.targets` gerados por pacotes somente após restore do repositório externo podem não participar, porque o restore externo não é executado de propósito.
- Repositórios que exijam workloads customizados, SDKs proprietários, feeds autenticados, submódulos ou preparação não convencional podem falhar na avaliação e devem ser documentados em vez de acomodados silenciosamente.
- A amostra fixada comprova compatibilidade com estados conhecidos dos repositórios, não com a ponta atual de suas branches padrão.
- O job externo separado depende da disponibilidade pública do GitHub, enquanto a suíte principal de fixtures sintéticas não depende.
- A amostra deve permanecer pequena o suficiente para ser revisável e operacionalmente barata; um novo repositório só deve ser incluído quando trouxer um formato de inspeção distinto.
