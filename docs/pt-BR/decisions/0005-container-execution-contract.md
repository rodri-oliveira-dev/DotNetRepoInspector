# ADR 0005: Definir o contrato de execução em container e compatibilidade de SDKs

**Idiomas:** [English](../../en/decisions/0005-container-execution-contract.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-04
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O DotNetRepoInspector é compilado para `net10.0`, mas sua matriz de inspeção suportada é mais ampla que seu próprio runtime. O gate de compatibilidade existente instala os SDKs .NET 8 e .NET 10 lado a lado e comprova que o `global.json` de um repositório pode selecionar qualquer uma dessas famílias independentemente do runtime do Inspector.

Essa distinção deve continuar verdadeira na imagem oficial de container. Uma imagem contendo somente runtime executaria o Inspector, mas quebraria a avaliação MSBuild de repositórios que selecionem um SDK ausente da imagem.

A containerização também não altera o modelo de confiança atual. A ADR 0001 e a documentação de segurança estabelecem que avaliação MSBuild não é sandbox. Um repositório pode influenciar imports, resolução de SDK, conditions, property functions, acesso ao filesystem e acesso de rede disponíveis para a identidade do processo. Docker pode fornecer uma fronteira de isolamento operacional somente na medida em que o próprio container esteja configurado com mounts, privilégios, credenciais e rede restritos.

Esta ADR define o contrato que as issues de implementação, CI, publicação e documentação devem preservar.

## Decisão

### Identidade da imagem oficial

Os nomes planejados das imagens oficiais são:

```text
ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector
docker.io/rodrigodotnet/dotnet-repo-inspector
```

Os dois registries devem representar a mesma versão do produto e a mesma revision do código-fonte. Publicação, política de tags, SBOM, provenance e verificação de release são responsabilidades das etapas seguintes; esta ADR fixa apenas a identidade e o contrato de runtime.

A imagem oficial é uma imagem Linux. As plataformas-alvo são:

- `linux/amd64`;
- `linux/arm64`.

Não existe exceção de arquitetura no momento desta decisão: a Microsoft publica artefatos suportados de container do .NET SDK para ambas as arquiteturas, incluindo as famílias .NET 8 e .NET 10 exigidas por este repositório. Se qualquer família de SDK obrigatória deixar de estar disponível ou utilizável em uma das plataformas-alvo, a publicação dessa plataforma deve falhar até que o contrato de compatibilidade seja deliberadamente revisado; publicar silenciosamente uma matriz reduzida não é permitido.

### Compatibilidade de SDKs dentro da imagem

A imagem deve conter SDKs estáveis das duas famílias suportadas:

| Plataforma do container | Runtime do Inspector | Famílias de SDK obrigatórias disponíveis aos repositórios inspecionados |
| --- | --- | --- |
| `linux/amd64` | `net10.0` | .NET 8 e .NET 10 |
| `linux/arm64` | `net10.0` | .NET 8 e .NET 10 |

A garantia é por família de SDK, não pelo feature band mínimo original usado por uma fixture. As fixtures de compatibilidade existentes fixam `8.0.100` e `10.0.100` com `rollForward: latestFeature`; portanto, um SDK estável e atualizado da família correspondente `8.0.x` ou `10.0.x` pode satisfazer a fixture pelas regras normais de resolução do `dotnet`.

A imagem não garante .NET 9, SDKs preview, workloads ou famílias arbitrárias de SDK, salvo se forem futuramente adicionadas à política de compatibilidade do repositório e este contrato for revisado.

A implementação deve preservar a semântica normal de seleção de SDK. Ela não deve reescrever o `global.json` do repositório inspecionado nem forçar toda avaliação MSBuild a usar o SDK .NET 10 do Inspector.

### Estratégia de imagem-base e manutenção dos SDKs

A imagem deve usar artefatos oficiais Linux da distribuição .NET da Microsoft. O SDK .NET 10 é a base natural de execução porque o próprio Inspector tem como target `net10.0`; um SDK estável .NET 8 deve então ser disponibilizado lado a lado a partir de uma fonte oficial da Microsoft.

O mecanismo exato de instalação é detalhe de implementação, mas deve atender a todos estes invariantes:

- a distribuição/variante Linux escolhida é suportada nas duas arquiteturas-alvo;
- ambas as famílias de SDK obrigatórias são instaladas a partir de artefatos oficiais da Microsoft;
- nenhuma tag-base `latest` é usada;
- todo `FROM` usa uma tag legível de versão/SO mais digest imutável quando o registry oferece suporte;
- qualquer SDK instalado separadamente é selecionado por uma versão estável explícita e obtido por um mecanismo verificável de distribuição da Microsoft;
- a imagem final expõe as duas famílias de SDK em `dotnet --list-sdks`;
- atualizações de SDK/base são manutenção esperada, não motivo para congelar indefinidamente layers antigas vulneráveis.

Fixação por digest fornece reprodutibilidade; não substitui servicing. A manutenção automatizada de imagens-base é tratada na issue dedicada seguinte e deve executar novamente os mesmos gates de compatibilidade/segurança antes do merge.

### Contrato de filesystem e mounts

O container possui dois paths persistentes distintos:

| Path | Finalidade | Acesso esperado |
| --- | --- | --- |
| `/repo` | repositório/source inspecionado | read-only por padrão |
| `/artifacts` | report de inspeção gerado e outros outputs explícitos | gravável |

`/repo` é o working directory padrão. O Inspector não deve exigir escrita no repositório inspecionado para uma inspeção básica. Espera-se que consumidores imponham essa fronteira com bind mount read-only.

O root filesystem do container deve suportar execução com Docker `--read-only`. Dados temporários de runtime que não possam ser evitados pelo .NET SDK ou tooling do sistema operacional devem ser efêmeros e montados explicitamente como `tmpfs`, normalmente sob `/tmp`; não são um path de dados persistente e não devem conter credenciais do chamador. Output persistente pertence somente a `/artifacts`, salvo se uma futura funcionalidade documentada introduzir outro mount de dados explícito.

Um baseline endurecido para execução local/offline é, portanto, equivalente a:

```bash
docker run --rm \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m \
  --mount type=bind,src="$PWD",dst=/repo,readonly \
  --mount type=bind,src="$PWD/artifacts",dst=/artifacts \
  <image> /repo --output /artifacts/inspection.json
```

O entrypoint da imagem final deve encaminhar argumentos ao contrato existente da CLI DotNetRepoInspector, para que o chamador não precise conhecer o DLL interno ou o layout de instalação da tool.

Cenários que precisem de caches graváveis adicionais, material de SDK privado ou estado de package feed são desvios explícitos do baseline. Devem usar mounts dedicados e de escopo estreito e não devem tornar `/repo` gravável apenas para satisfazer tooling.

### Execução non-root e comportamento de UID/GID

A imagem final deve declarar e executar como usuário non-root por padrão. Pode usar a conta non-root fornecida pela imagem-base oficial .NET selecionada ou uma conta equivalente interna à imagem; root não deve ser necessário para inspeção.

O runtime também deve tolerar UID/GID numéricos explícitos fornecidos pelo chamador onde Docker oferecer suporte, por exemplo:

```bash
docker run --user "$(id -u):$(id -g)" ...
```

Isso permite que arquivos criados no diretório `/artifacts` montado a partir do host pertençam ao usuário host que executou o comando em vez da identidade numérica padrão da imagem.

A imagem não deve depender de um home directory root-owned gravável nem de uma entrada no arquivo de usuários para o UID efetivo de runtime. Estado gravável de CLI/home/temp exigido pelo .NET deve apontar para a área temporária efêmera. O chamador é responsável por tornar `/artifacts` gravável pelo UID/GID efetivo; a imagem não deve resolver problemas de permissão do host alternando para root ou executando chmod/chown no repositório montado.

### Baseline de privilégios e integração com host

A inspeção básica deve funcionar sem:

- privileged mode;
- Docker socket ou socket de outro container engine;
- namespaces PID/IPC do host;
- capabilities Linux adicionais;
- source mounts graváveis;
- diretórios de credenciais do host;
- credenciais de repositório, cloud, SSH, signing, deployment, Docker ou Kubernetes.

O baseline de validação usa `--cap-drop=ALL` e `--security-opt=no-new-privileges`. Nenhuma implementação pode exigir `/var/run/docker.sock` ou `--privileged` como conveniência para inspecionar repositórios.

### Política de rede

Inspeção local é uma operação capaz de funcionar offline. Quando os SDKs selecionados pelo repositório, imports e demais dependências de avaliação já estiverem presentes, o container deve conseguir inspecionar usando `--network none`.

A imagem não deve executar restore implícito nem upload automático apenas porque acesso de rede esteja disponível. Avaliação MSBuild controlada pelo repositório ainda pode tentar acessar a rede caso o namespace do container permita; esse é outro motivo para o baseline endurecido desabilitar networking.

Acesso de rede é opt-in para funcionalidades que inerentemente precisem dele. O sink HTTP built-in é o exemplo canônico e continua sendo selecionado explicitamente com as opções existentes da CLI, como `--sink http` e `--sink-url`. Cenários com SDK/package feed privados também podem exigir rede, mas não fazem parte da garantia de compatibilidade offline e devem ser configurados explicitamente.

Habilitar rede não relaxa nenhum outro controle: execução non-root, mounts restritos, least privilege e tratamento de secrets continuam válidos.

### Política de secrets e credenciais

Nenhuma credencial pode ser incorporada a layer da imagem, build argument, valor-padrão de environment da imagem, label/annotation OCI, manifest ou exemplo de command line.

As regras existentes de credenciais do Inspector continuam autoritativas:

- credenciais de sink existem somente em runtime;
- credenciais nunca são passadas como argumentos de CLI nem incorporadas em `--sink-url`;
- o bearer token do sink HTTP usa a variável de ambiente de runtime existente `DOTNET_REPO_INSPECTOR_HTTP_TOKEN`;
- credenciais de feed/SDK privado, quando inevitáveis, devem ser de curta duração e limitadas somente à fonte necessária;
- diretórios amplos de credenciais do host, como Docker, cloud, SSH, Kubernetes ou perfis de package manager, não devem ser montados como atalho;
- credenciais não devem ser gravadas em `/artifacts`, logs, diagnostics, metadados de imagem nem estado temporário persistido.

Passar uma environment variable de runtime ao container não torna seguro expor credenciais arbitrárias ao MSBuild. A filtragem de child processes do DotNetRepoInspector continua sendo apenas defense in depth. A inspeção offline mais segura não possui credencial alguma dentro do container.

### Containerização não é sandbox para MSBuild

**A imagem oficial de container não é um sandbox de segurança para avaliação MSBuild.**

O container limita apenas aquilo que o repositório avaliado consegue alcançar quando o chamador limita aquilo que o container consegue alcançar. Qualquer arquivo montado no container, valor de environment visível ao processo, destino de rede alcançável pelo namespace ou capability concedida ao container pode ser alcançável por avaliação MSBuild controlada pelo repositório.

Consequentemente, inspecionar código não confiável ainda exige ambiente isolado, efêmero, non-privileged e sem dados sensíveis. O hardening Docker desta ADR reduz exposição, mas não transforma avaliação de lógica MSBuild não confiável em uma operação confiável.

## Contrato de validação de compatibilidade

As issues seguintes de implementação e CI devem comprovar o contrato, não apenas inferi-lo pelo conteúdo da imagem.

Para `linux/amd64` e `linux/arm64`, a validação deve demonstrar no mínimo:

1. a imagem é construída a partir das referências-base fixadas/declaradas;
2. o usuário efetivo de runtime é non-root;
3. `dotnet --list-sdks` contém pelo menos um SDK estável .NET 8 e um SDK estável .NET 10;
4. o repositório existente `tests/Fixtures/Compatibility/Net8` resolve para a família .NET 8;
5. o repositório existente `tests/Fixtures/Compatibility/Net10` resolve para a família .NET 10;
6. ambas as fixtures podem ser inspecionadas sem tornar `/repo` gravável;
7. uma inspeção representativa bem-sucedida grava seu report somente em `/artifacts`;
8. uma fixture preparada e compatível funciona com root filesystem `--read-only`, scratch efêmero e `--network none`;
9. o baseline não precisa de Docker socket, privileged mode, mount de credenciais do host ou secret;
10. falha de qualquer família de SDK obrigatória ou de qualquer arquitetura-alvo bloqueia publicação oficial multi-platform.

Emulação por Buildx/QEMU é aceitável no CI quando runners nativos não estiverem disponíveis, desde que a imagem resultante de cada arquitetura-alvo seja realmente executada no smoke test, e não apenas construída. A verificação de release também deve confirmar que o índice OCI publicado contém as duas plataformas obrigatórias.

A matriz cross-platform de host existente no repositório (Linux, Windows, macOS) continua sendo o contrato para execução direta via CLI/.NET Tool. Esta ADR adiciona uma matriz de plataformas de container Linux; ela não remove nem substitui a matriz de compatibilidade de host.

## Alternativas consideradas

### Imagem final contendo somente runtime

Rejeitada. O DotNetRepoInspector executa avaliação MSBuild e deve honrar repositórios que selecionem famílias de SDK suportadas. Uma imagem pequena contendo somente runtime tornaria a distribuição enganosa ao perder a garantia atual de compatibilidade .NET 8/.NET 10.

### Imagem oficial separada por família de SDK

Rejeitada para a distribuição inicial. Isso obrigaria o chamador a conhecer o SDK do repositório antes da inspeção e divergiria do comportamento host atual com SDKs lado a lado. Uma única imagem contendo as duas famílias suportadas preserva o contrato atual.

### Executar como root e confiar no isolamento do Docker

Rejeitada. Root é desnecessário para a CLI e aumenta o impacto de mount amplo ou configuração incorreta do container.

### Source mount gravável

Rejeitado como padrão. Inspeção é uma operação de leitura de metadados. Outputs pertencem a um mount separado de artifacts, tornando a intenção de escrita explícita e mais fácil de restringir.

### Rede habilitada como requisito

Rejeitada. A inspeção básica deve continuar capaz de funcionar localmente/offline. Sinks dependentes de rede e resolução de dependências privadas são cenários opt-in separados.

### Tratar o container como sandbox para MSBuild não confiável

Rejeitado. Isso contrariaria o modelo real de confiança do MSBuild e a documentação de segurança existente. O hardening do container reduz recursos acessíveis, mas não torna a avaliação controlada pelo repositório intrinsecamente segura.

## Consequências

### Positivas

- o container preserva a garantia atual de avaliação .NET 8/.NET 10;
- a implementação recebe contratos objetivos de mounts, identidade, rede, secrets e plataformas;
- source e writes de output ficam explicitamente separados;
- execução non-root e com root filesystem read-only pode ser testada antes da publicação;
- consumidores podem executar um baseline realmente offline e endurecido quando dependências estiverem previamente disponíveis;
- GHCR e Docker Hub passam a ter uma única identidade de imagem e modelo de release planejados;
- publicação multi-platform possui requisito explícito e fail-closed de compatibilidade.

### Trade-offs

- a imagem oficial é maior que uma imagem CLI contendo somente runtime porque inclui múltiplas famílias de SDK;
- suporte a `--read-only` exige tratamento explícito de scratch efêmero;
- execução com UID/GID arbitrários do host exige que a imagem não dependa de home directory gravável convencional;
- repositórios dependentes de SDKs privados, feeds, workloads ou imports de rede precisam de configuração adicional e explícita em runtime e ficam fora do baseline offline;
- a imagem reduz exposição operacional, mas não pode prometer avaliação segura de lógica MSBuild hostil.

## Trabalho futuro

- **#101** — implementar o Dockerfile e a execução local endurecida exatamente conforme este contrato.
- **#102** — transformar os smoke tests de plataforma/SDK/mount/non-root/read-only/offline/segurança em checks de CI que bloqueiem release.
- **#103** — automatizar servicing seguro de imagens-base/digests.
- **#104** — publicar a mesma release multi-platform nos dois registries definidos aqui.
- **#105** — adicionar SBOM/provenance por digest e verificação de supply chain.
- **#106** — publicar a documentação de container/segurança para o usuário final e concluir release readiness.

## Referências

- Política de compatibilidade de SDK/SO: [`../compatibility.md`](../compatibility.md)
- Modelo de segurança: [`../security.md`](../security.md)
- Política de segurança do repositório: [`../../../SECURITY.pt-BR.md`](../../../SECURITY.pt-BR.md)
- Estratégia de avaliação MSBuild: [ADR 0001](0001-msbuild-evaluation-strategy.md)
- Arquitetura de persistência/sink: [ADR 0003](0003-persistence-sink-architecture.md)
- Microsoft Learn — imagens de container .NET: https://learn.microsoft.com/dotnet/core/docker/container-images
- Microsoft Artifact Registry — imagens do .NET SDK: https://mcr.microsoft.com/artifact/mar/dotnet/sdk
- Issue #100: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/100
