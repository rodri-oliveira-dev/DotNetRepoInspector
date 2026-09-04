# Imagem de container local

**Idiomas:** [English](../en/container.md) | Português (Brasil)

O repositório contém a implementação da imagem de container oficial planejada do DotNetRepoInspector. Esta etapa é somente para build e validação local; a issue #101 **não** publica imagem no GHCR nem no Docker Hub.

O contrato de runtime é definido pela [ADR 0005](decisions/0005-container-execution-contract.md).

## Conteúdo da imagem

A imagem:

- executa o DotNetRepoInspector em .NET 10;
- contém as famílias estáveis de SDK .NET 8 e .NET 10 lado a lado, preservando a autoridade do `global.json` do repositório;
- usa imagens Microsoft .NET SDK com tags explícitas de versão/SO e digests multi-platform imutáveis;
- executa por padrão com a identidade não-root `app` fornecida pela Microsoft (`APP_UID`, atualmente 1654);
- usa `/repo` para o source read-only e `/artifacts` para saída explicitamente gravável;
- redireciona home da CLI, cache do NuGet e outros estados transitórios para `/tmp`, permitindo filesystem raiz read-only quando `/tmp` é montado como `tmpfs`;
- inicia diretamente a CLI existente, portanto os argumentos do container são os mesmos argumentos normais do DotNetRepoInspector.

## Build local

Na raiz do repositório:

```bash
docker build --pull -t dotnet-repo-inspector:local .
```

Esse comando e o workflow normal de validação do repositório não fazem push para registry.

## Verificar a matriz de SDKs

A CLI é o entrypoint da imagem. Sobrescreva-o somente para diagnóstico da imagem, como a verificação dos SDKs instalados:

```bash
docker run --rm \
  --entrypoint dotnet \
  dotnet-repo-inspector:local \
  --list-sdks
```

A saída deve conter pelo menos um SDK estável `8.0.x` e um SDK estável `10.0.x`.

As fixtures de compatibilidade usam intencionalmente a semântica normal de roll-forward do `global.json`:

- `tests/Fixtures/Compatibility/Net8` seleciona a família .NET 8;
- `tests/Fixtures/Compatibility/Net10` seleciona a família .NET 10.

A imagem não reescreve `global.json` nem força os repositórios inspecionados a usar o SDK .NET 10 do Inspector.

## Smoke checks da CLI

```bash
docker run --rm dotnet-repo-inspector:local --help
docker run --rm dotnet-repo-inspector:local --version
```

O container preserva o contrato atual de saída e exit codes da CLI.

## Inspeção offline endurecida

Crie primeiro o diretório de saída no host:

```bash
mkdir -p artifacts
```

No Linux, use o UID/GID do usuário do host para que o diretório de saída montado por bind continue gravável sem executar a imagem como root. Depois inspecione um repositório ou fixture preparado usando source read-only, volume de artifacts gravável, filesystem do container read-only, sem rede, sem Linux capabilities e sem elevação de privilégios:

```bash
docker run --rm \
  --user "$(id -u):$(id -g)" \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m,mode=1777 \
  --mount type=bind,src="$PWD/tests/Fixtures/Compatibility/Net8",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  dotnet-repo-inspector:local \
  /repo --output /artifacts/net8-inspection.json
```

Uma execução bem-sucedida grava o `InspectionReport` normalizado no diretório `artifacts` do host sem exigir repositório gravável, Docker socket, diretório de credenciais do host, privileged mode ou acesso à rede.

Repita o mesmo comando com `tests/Fixtures/Compatibility/Net10` para exercitar a resolução do SDK .NET 10.

## UID/GID do host e ownership dos artifacts

A própria imagem declara como usuário padrão a identidade não-root `app` fornecida pela Microsoft. Informar `--user "$(id -u):$(id -g)"` é um override de runtime para ownership do bind mount; isso não torna o container privilegiado e a CLI não depende de uma entrada em passwd nem de home root-owned gravável.

O diretório `/artifacts` do host deve ser gravável pelo UID/GID selecionado. A imagem não troca para root nem executa chmod/chown no repositório montado para contornar permissões do host.

## Cenários que precisam de rede

A inspeção básica de repositórios previamente preparados é compatível com uso offline e deve preferir `--network none`.

Acesso à rede é um desvio explícito para funcionalidades que realmente precisam dele, como o sink HTTP opt-in ou SDKs/package sources privados. Para o sink HTTP, preserve o contrato existente de credenciais: forneça o bearer token apenas em runtime por `DOTNET_REPO_INSPECTOR_HTTP_TOKEN`; nunca incorpore credenciais à imagem, a build args, argumentos da CLI ou ao `--sink-url`.

Formato de exemplo:

```bash
docker run --rm \
  -e DOTNET_REPO_INSPECTOR_HTTP_TOKEN \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  dotnet-repo-inspector:local \
  /repo \
  --output /artifacts/inspection.json \
  --sink http \
  --sink-url https://evidence.example/api/snapshots
```

Não monte diretórios amplos de credenciais Docker, cloud, SSH, Kubernetes, NuGet ou outros do host como atalho.

## Fronteira de segurança

**Containerização não transforma avaliação MSBuild em um sandbox de segurança.**

A avaliação MSBuild controlada pelo repositório pode acessar recursos disponíveis para a identidade do container. Mounts endurecidos, execução non-root, filesystem raiz read-only, capabilities removidas e rede desabilitada reduzem exposição, mas repositórios não confiáveis ainda exigem ambiente isolado, efêmero e sem dados sensíveis ou credenciais.

Consulte [`security.md`](security.md) e a [ADR 0005](decisions/0005-container-execution-contract.md) para o modelo completo de confiança.

## Validação no CI

O [`validate-container.yml`](../../.github/workflows/validate-container.yml) transforma a imagem em um gate de qualidade/segurança para pull requests. Ele:

- executa Hadolint tratando warnings como falha;
- valida cada referência de imagem base Microsoft .NET lida do `Dockerfile` proposto, exigindo tag legível e digest `sha256` imutável;
- faz build com `--pull` a partir dessas referências pinadas;
- executa de fato a imagem em `linux/amd64` e `linux/arm64` via Buildx/QEMU;
- roda a suíte reutilizável [`container_smoke.sh`](../../.github/scripts/container_smoke.sh) para validar non-root, `--help`, `--version`, resolução dos SDKs .NET 8/.NET 10, geração bem-sucedida do report, source/root filesystem read-only, operação offline e os exit codes documentados `0` a `5`;
- executa um relatório Trivy visível com todos os findings `HIGH`/`CRITICAL`, inclusive os que ainda não possuem correção upstream;
- executa um segundo gate Trivy com `ignore-unfixed`, de modo que somente vulnerabilidades `HIGH`/`CRITICAL` corrigíveis bloqueiem o workflow.

O workflow possui apenas permissão de leitura no repositório e nunca autentica nem faz push para registry. As Actions de terceiros usadas pelo gate estão pinadas por commit SHA.

## Manutenção das imagens base com Dependabot

O [`.github/dependabot.yml`](../../.github/dependabot.yml) monitora o `Dockerfile` da raiz com o ecossistema `docker`. Durante o período inicial de estabilização do container, a verificação ocorre em dias úteis às `08:00` no timezone `America/Sao_Paulo`, com no máximo três pull requests simultâneos de atualização de versão Docker.

As referências das bases permanecem no formato `image:version-tag@sha256:digest`. O Dependabot pode propor atualização de tag/digest, mas qualquer pull request que altere o `Dockerfile` precisa passar pelos mesmos gates de Hadolint, build multi-arquitetura, smoke tests e Trivy antes do merge. O workflow lê as referências diretamente do `Dockerfile` proposto, em vez de duplicar versões ou digests no CI, evitando validar uma atualização contra uma referência hardcoded antiga.

A documentação do GitHub confirma que o ecossistema Docker do Dependabot procura manifests `Dockerfile` no `directory` configurado, e o updater Docker do Dependabot preserva e atualiza um digest existente quando a imagem já está pinada por digest. Como o Dependabot avalia o `dependabot.yml` na branch padrão, a criação real dos pull requests Docker só começa quando esta configuração chegar à branch padrão; esse comportamento documentado é a validação equivalente utilizada enquanto o trabalho ainda está na branch de integração.

A cadência inicial `daily` pode voltar para `weekly` quando o rollout do container estiver estável: pelo menos quatro semanas consecutivas sem necessidade recorrente de correção manual das imagens base ou ajuste do security gate e sem backlog conhecido de findings `HIGH`/`CRITICAL` corrigíveis. Reduzir a frequência não pode relaxar o digest pinning nem qualquer gate de CI/segurança.
