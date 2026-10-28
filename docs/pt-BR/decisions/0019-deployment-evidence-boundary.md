# ADR 0019: Manter evidências de deployment factuais, opt-in e independentes de adapters

**Idiomas:** [English](../../en/decisions/0019-deployment-evidence-boundary.md) | Português (Brasil)

- **Status:** Accepted
- **Data:** 2026-10-27
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

Metadados de SDK e target framework, isoladamente, não descrevem as evidências de runtime relevantes para um inventário de frota. Configurações avaliadas de publicação e referências de imagens base em Dockerfiles fornecem fatos adicionais úteis, mas nenhuma delas prova como uma aplicação foi publicada ou o que está implantado. A análise de Dockerfiles também introduz uma fronteira de leitura de arquivos que não deve afetar o custo da inspeção padrão nem acoplar o Core a uma implementação Docker.

A ADR 0017 separa evidência de decisões de policy, e a ADR 0018 define uma fronteira opt-in para Integration Discovery de C#. Evidências de deployment precisam da mesma separação duradoura, mantendo o MSBuild como fonte de verdade para propriedades de publicação.

## Decisão

### Fatos de publicação

- `DotNetRepoInspector.MSBuild` avalia as propriedades permitidas `SelfContained`, `PublishSelfContained`, `PublishAot`, `PublishTrimmed` e `RuntimeFrameworkVersion` pelo processo existente de avaliação MSBuild.
- Fatos booleanos e string preservam os estados `evaluated`, `unspecified`, `unknown` e `context-dependent`. Valores são emitidos somente para `evaluated`.
- A avaliação multi-target preserva um valor no nível do projeto somente quando os contextos de target framework concordam. Conflitos são representados como `context-dependent`, sem resolução por precedência ou inferência.
- O Core possui somente contratos normalizados de publicação e não depende de tipos nem da execução do MSBuild.

### Evidências de contêiner

- `DotNetRepoInspector.ContainerDiscovery` possui a enumeração limitada e o parsing de Dockerfiles. Ele não depende de Core, clientes Docker, clientes de registry nem SDKs de providers.
- A Engine invoca esse pipeline somente após opt-in explícito via CLI/request ou configuração do repositório. Sem opt-in, o pipeline não é chamado e nenhum path de Dockerfile é enumerado.
- A gramática suportada é deliberadamente pequena: nomes elegíveis de Dockerfile, comentários, continuações, defaults de `ARG` globais, `FROM`, `--platform`, aliases, referências a stages anteriores, `scratch`, tags e digests SHA-256.
- Input não suportado ou dinâmico produz evidência não resolvida sem reter a expressão. Defaults de argumentos com aparência de segredo nunca são substituídos no output.
- A descoberta é read-only, não segue links de filesystem, permanece abaixo da raiz do repositório, observa cancelamento e aplica limites de paths/arquivos/bytes/profundidade/resultados/diagnósticos/tempo. Resultados parciais possuem estado explícito de truncation.
- O pipeline nunca invoca Docker ou shell, executa código do repositório, lê valores de variáveis de ambiente do processo para interpolação, consulta registries nem resolve digests remotos.

### Contratos e policy

- O Core possui somente `ProjectPublicationMetadata`, `ContainerImageEvidence` e metadados de execução normalizados. Esses contratos não contêm tipos de parser, Docker, MSBuild, GitHub, registry ou scanner de vulnerabilidades.
- O schema `1.7` é aditivo. Payloads `1.x` compatíveis mais antigos podem omitir os novos campos; a desserialização fornece defaults conservadores.
- Evidência é distinta de `policyFindings` e diagnósticos. O Inspector não ingere CVEs, decide aplicabilidade de patches, infere exposição do runtime nem declara vulnerabilidade.
- Adapters de delivery reutilizam a fronteira de Engine/request/configuração. Eles não implementam outro parser nem uma policy de segurança própria.

## Alternativas consideradas

### Sempre analisar Dockerfiles

Rejeitada porque alteraria o acesso ao filesystem, o custo e as premissas de confiança padrão de toda inspeção.

### Usar Docker ou uma API de registry para resolver imagens

Rejeitada porque introduziria dependências de daemon/rede, estado externo mutável, credenciais e uma falsa impressão de que uma imagem referenciada está implantada.

### Colocar parsing de Dockerfile ou tipos MSBuild no Core

Rejeitada porque o Core é a fronteira de contratos normalizados e policies. Dependências de infraestrutura inverteriam a direção pretendida das dependências e restringiriam outros adapters.

### Inferir status de vulnerabilidade por versões ou tags

Rejeitada porque evidência do repositório não estabelece os bytes implantados, aplicabilidade de patches, explorabilidade nem status autoritativo de segurança. Essa correlação pertence a um sistema downstream com metadados externos de release e provenance de deployment.

## Consequências

### Positivas

- O comportamento padrão da inspeção e o custo de Dockerfiles permanecem inalterados.
- Inventários downstream recebem evidência determinística e atribuída à origem sem uma policy de CVE embutida.
- Fatos unknown e context-dependent permanecem explícitos.
- Detalhes de implementação de Docker, registry, GitHub e MSBuild permanecem fora do Core.

### Trade-offs

- O suporte a Dockerfile é intencionalmente incompleto e pode emitir evidência não resolvida para sintaxe avançada válida.
- Tags de imagem e digests declarados são referências, não prova do conteúdo do registry ou do deployment.
- A agregação de publicação no nível do projeto não expõe um registro público separado para cada contexto de target framework; divergência é representada conservadoramente como `context-dependent`.
- O workflow central de inventário em `.github` precisa de um follow-up separado para consumir e correlacionar a evidência do schema `1.7`.

## Referências

- Issue #218
- [ADR 0017 — fronteira de extensibilidade da policy engine](0017-policy-engine-extensibility-boundary.md)
- [ADR 0018 — fronteira de Integration Discovery](0018-integration-discovery-boundary.md)
- [Evidências de deployment](../deployment-evidence.md)
- [Schema de inspeção](../schema/inspection-v1.md)
