# Evidências de deployment

**Idiomas:** [English](../en/deployment-evidence.md) | Português (Brasil)

O schema `1.7` adiciona evidências factuais que sistemas downstream de inventário podem correlacionar com dados de suporte de runtime ou segurança mantidos separadamente. O DotNetRepoInspector **não** determina se uma aplicação está vulnerável, explorável ou se foi implantada a partir do repositório inspecionado.

## Propriedades de publicação

Todo projeto contém um objeto `publication` construído a partir de propriedades avaliadas pelo MSBuild:

| Campo do contrato | Propriedade avaliada | Significado |
| --- | --- | --- |
| `selfContained` | `SelfContained` | Configuração self-contained efetiva no contexto avaliado. Defaults do SDK podem produzir `false` avaliado. |
| `publishSelfContained` | `PublishSelfContained` | Configuração self-contained específica de publicação quando disponível. |
| `publishAot` | `PublishAot` | Solicitação de publicação Native AOT quando disponível. |
| `publishTrimmed` | `PublishTrimmed` | Solicitação de trimming quando disponível. |
| `runtimeFrameworkVersion` | `RuntimeFrameworkVersion` | Versão solicitada do runtime framework quando disponível. |

Cada campo possui um estado:

- `evaluated`: o MSBuild retornou um valor suportado; `value` está presente, inclusive para `false` explícito;
- `unspecified`: a propriedade avaliada estava vazia;
- `unknown`: o valor avaliado não pôde ser representado com segurança ou, para booleanos, não era válido;
- `context-dependent`: avaliações de target frameworks produziram estados ou valores diferentes.

Projetos multi-target são avaliados uma vez no outer build e uma vez para cada target framework. O valor de publicação no nível do projeto só é mantido quando esses contextos concordam; divergência se torna `context-dependent`. Target frameworks e runtime identifiers permanecem evidências separadas. Essas propriedades não provam os argumentos usados por um `dotnet publish` posterior, os arquivos copiados para uma imagem ou o runtime atualmente implantado.

## Descoberta de imagens Docker

Container Discovery exige opt-in:

```bash
dotnet repo-inspect . --discover-containers --output artifacts/inspection.json
```

Ela também pode ser habilitada por `containerDiscovery.enabled` na configuração. Quando desabilitada, a Engine não invoca o pipeline de descoberta, não enumera Dockerfiles e emite `containerImages` vazio com `containerDiscovery.enabled=false`.

O parser limitado reconhece arquivos chamados `Dockerfile`, `Dockerfile.*` ou `*.Dockerfile`; defaults de `ARG` globais declarados antes do primeiro `FROM`; `FROM`; `--platform=...`; comentários; continuações de linha; aliases de stages; referências a stages anteriores; `scratch`; tags; e digests SHA-256. Ele classifica `sdk`, `aspnet`, `runtime` e `runtime-deps` somente para a família exata de repositórios oficiais `mcr.microsoft.com/dotnet/...`. Nomes semelhantes em outros registries permanecem imagens externas comuns.

Flags não suportadas, referências malformadas, argumentos ausentes/dinâmicos, expressões de shell e nomes de `ARG` com aparência de segredo não são adivinhados. Eles produzem `resolutionStatus=unresolved` e omitem a expressão não resolvida da evidência normalizada. Um diagnóstico do parser pode identificar o arquivo e a linha de origem usando reason codes controlados.

A descoberta é determinística e read-only. Ela ordena paths e resultados ordinalmente, ignora diretórios de build/gerados e links de filesystem, rejeita paths fora do repositório, aceita cancelamento do chamador e aplica os seguintes limites padrão:

| Recurso | Limite padrão |
| --- | ---: |
| Paths visitados no filesystem | 10.000 |
| Dockerfiles | 500 |
| Bytes por arquivo | 1 MiB |
| Total de bytes | 20 MiB |
| Profundidade de diretórios | 32 |
| Resultados | 1.000 |
| Diagnósticos | 100 |
| Tempo de descoberta | 30 segundos |

Atingir um limite preserva as evidências já coletadas, emite um diagnóstico controlado e define `containerDiscovery.truncated=true`.

## Fronteira de segurança e limitações

Container Discovery nunca invoca Docker ou shell, faz build ou pull de imagem, executa código do repositório, expande variáveis de ambiente do processo, consulta um registry ou resolve um digest remoto. Ela lê somente texto limitado de Dockerfiles elegíveis. Ela não interpreta toda a sintaxe BuildKit, executa comandos do Dockerfile, inspeciona manifests Compose/Kubernetes, inspeciona o filesystem de uma imagem nem prova o que foi implantado.

Somente referências normalizadas e locais de origem entram no `InspectionReport`. Corpos de source e expressões não resolvidas não são serializados. Defaults de `ARG` cujos nomes pareçam conter credenciais são deliberadamente tratados como não resolvidos para que seus valores não se tornem evidência. Consulte [Segurança e privacidade](security.md) para o modelo de confiança separado do MSBuild e as orientações para repositórios não confiáveis.

## Exemplo de entrada para auditoria de runtimes

O comando abaixo coleta evidências que um processo de inventário posterior e separado pode correlacionar com metadados oficiais de suporte de runtime:

```bash
dotnet repo-inspect . --discover-containers --output artifacts/inspection.json
```

Um sistema downstream pode ler `projects[].targetFrameworks`, `projects[].runtimeIdentifiers`, `projects[].publication` e entradas externas resolvidas em `containerImages[]`. Por exemplo, um registro de inventário pode preservar o estado avaliado de `publishAot` junto de `mcr.microsoft.com/dotnet/aspnet:10.0` e seu local no Dockerfile.

Essa correlação não é um resultado de vulnerabilidade. O Inspector não consulta fontes de CVE, não determina aplicabilidade de patches, não inspeciona o artifact efetivamente implantado nem afirma que uma aplicação está vulnerável. Conclusões de segurança exigem dados externos e autoritativos de release, provenance de deployment e uma policy pertencente ao consumidor downstream.

## Exposição atual nos adapters

A CLI expõe `--discover-containers`. Outros adapters podem habilitar a capacidade pelo contrato compartilhado de configuração/request sem implementar outro parser. O workflow central de inventário em `.github` é intencionalmente uma integração posterior após o lançamento desta feature; ele não é modificado pela issue #218.
