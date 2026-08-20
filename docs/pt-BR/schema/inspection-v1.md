# Contrato JSON de inspeção — schema 1.x

**Idiomas:** [English](../../en/schema/inspection-v1.md) | Português (Brasil)

`DotNetRepoInspector.Core.Contracts` define o resultado estável da inspeção de forma independente dos detalhes internos do MSBuild, GitHub Actions, persistência ou qualquer mecanismo específico de delivery.

A versão atual do schema é `1.3`.

## Contrato de nível superior

Todo payload contém estas propriedades:

- `schemaVersion`: versão do contrato JSON público.
- `repository`: metadados do repositório. Campos individuais podem estar ausentes quando não estão disponíveis, não são aplicáveis ou não puderam ser coletados.
- `dotNetSdk`: configuração do SDK e versão resolvida pelo ambiente.
- `projects`: projetos normalizados, sempre emitidos como array.
- `diagnostics`: diagnósticos no nível do repositório, sempre emitidos como array.

O schema legível por máquina está em [`inspection-v1.schema.json`](inspection-v1.schema.json). Um payload canônico está disponível em [`examples/inspection-v1.example.json`](examples/inspection-v1.example.json).

## Metadados Git do repositório

Os metadados do repositório são normalizados independentemente da implementação do Git:

- `name`: identidade do repositório inferida do remote `origin`, quando disponível; caso contrário, do nome do diretório raiz da work tree do Git;
- `commitSha`: SHA completo do commit de `HEAD` quando o repositório possui um commit;
- `branch`: nome simbólico curto da branch quando `HEAD` está associado; omitido em detached HEAD;
- `remoteUrl`: URL de `origin` quando disponível. Informações de usuário HTTP(S) são removidas antes que o valor entre no contrato normalizado;
- `isDirty`: `true` quando existem alterações tracked/index/untracked na work tree, `false` quando a work tree está limpa e omitido quando o estado não pôde ser determinado.

Um diretório que não esteja dentro de um repositório Git continua sendo um alvo de inspeção válido. Nesse caso, o objeto `repository` pode não conter propriedades derivadas do Git.

## Classificação e overrides explícitos

`projects[].classification.kind` é a classificação efetiva. Normalmente ela é produzida pelo classificador determinístico e `confidence` e `signals` descrevem a decisão automática.

O schema `1.3` adiciona dois campos opcionais usados somente quando um override explícito altera esse resultado efetivo:

- `source`: `configuration` quando o override veio do arquivo de configuração do repositório, ou `request` quando veio diretamente da camada de CLI/Action/request;
- `automaticKind`: o tipo de classificação produzido pelo classificador automático antes da aplicação do override.

Os `signals` automáticos continuam presentes mesmo quando existe override. O Inspector não reescreve fatos do MSBuild para fazê-los concordar com um override configurado, e a `confidence` automática é omitida em vez de ser apresentada como confiança na escolha manual.

Consulte [`../configuration.md`](../configuration.md) para o formato de configuração e a precedência.

## Valores opcionais e ausentes

O contrato distingue deliberadamente ausência de um valor explícito:

- uma propriedade opcional omitida significa que o valor não está disponível, não é aplicável ou não foi coletado;
- `false` é um booleano avaliado explicitamente e é diferente de uma propriedade omitida;
- `[]` significa que a coleção foi produzida e não contém entradas;
- valores JSON `null` não são emitidos pelo serializer canônico.

Essa distinção é especialmente importante para fatos MSBuild como `isTestProject` e `isPackable`, para `repository.isDirty` e para os campos de proveniência da classificação, que só existem quando um override está ativo.

## Diagnósticos

Diagnósticos são fatos estáveis da inspeção, e não linhas de log operacional. Um diagnóstico contém um código estável `DRIxxxx`, uma das severidades `info`, `warning` ou `error`, uma mensagem estável legível por pessoas e os campos opcionais `source`, `details` e `context`.

A automação deve tomar decisões com base em `code` e `severity`, nunca em texto localizado. `context` contém strings estruturadas e não sensíveis que ajudam a identificar o componente ou fato afetado. Saída bruta de processos filhos, conteúdo de código-fonte, variáveis de ambiente, credenciais, tokens e outros secrets não devem ser copiados para o contrato normalizado de diagnósticos.

O catálogo estável de diagnósticos e as regras de logging operacional estão documentados em [`../diagnostics.md`](../diagnostics.md).

## Caminhos

Caminhos no contrato normalizado usam `/` como separador e não devem conter caminhos absolutos do workspace específicos da máquina.

Caminhos de projetos e de referências entre projetos são relativos à raiz do repositório. `dotNetSdk.globalJsonPath` é relativo à raiz do repositório inspecionado; portanto, um `global.json` aplicável em um diretório ancestral pode ser representado com segmentos `../`.

A raiz da work tree descoberta pelo adapter Git é um valor operacional interno e não é serializada no contrato público de inspeção.

## Serialização determinística

`InspectionJsonSerializer` canonicaliza as coleções antes da serialização:

- projetos são ordenados por `path`;
- SDKs dos projetos são ordenados por nome e versão;
- target frameworks e runtime identifiers são ordenados ordinalmente;
- referências entre projetos são ordenadas por caminho;
- sinais de classificação são ordenados ordinalmente;
- diagnósticos são ordenados por severidade, código, source, message, details e contexto canônico;
- chaves de contexto dos diagnósticos são ordenadas ordinalmente;
- separadores de caminho são normalizados para `/`;
- nomes de propriedades usam `camelCase`;
- propriedades opcionais com valor `null` são omitidas.

Assim, a mesma informação normalizada produz JSON byte a byte equivalente independentemente da ordem de descoberta.

## Política de compatibilidade

As versões do schema seguem uma política major/minor.

- Campos opcionais e aditivos podem ser introduzidos em uma nova versão `1.x`.
- O schema `1.1` adicionou o objeto opcional `context` ao diagnóstico e restringiu a severidade ao vocabulário documentado.
- O schema `1.2` adicionou o booleano opcional `repository.isDirty`, preenchido pela inspeção de metadados Git.
- O schema `1.3` adiciona os campos opcionais `classification.source` e `classification.automaticKind` para distinguir overrides explícitos da classificação automática.
- Consumidores do schema `1.x` devem ignorar campos desconhecidos e preservar a semântica documentada dos campos existentes.
- Remover ou renomear um campo, alterar seu tipo, tornar obrigatório um campo opcional ou alterar seu significado é uma breaking change e exige uma nova versão major do schema, como `2.0`.
- `InspectionSchema.IsCompatibleVersion` aceita versões com a major atual e rejeita uma major diferente.

## Mapeamento dos fatos atuais da inspeção

O contrato estável intencionalmente não expõe tipos de resultado específicos da infraestrutura.

| Fato de origem | Contrato normalizado |
| --- | --- |
| Identidade do repositório Git | `repository.name` |
| Commit Git de `HEAD` | `repository.commitSha` |
| `HEAD` simbólico do Git | `repository.branch` |
| Remote Git `origin` | `repository.remoteUrl` após sanitização de credenciais |
| Estado da work tree do Git | `repository.isDirty` |
| SDK `GlobalJsonPath` | `dotNetSdk.globalJsonPath` após normalização do caminho |
| SDK `Version` configurado | `dotNetSdk.configured.version` |
| SDK `RollForward` configurado | `dotNetSdk.configured.rollForward` |
| SDK `AllowPrerelease` configurado | `dotNetSdk.configured.allowPrerelease` |
| SDK `ResolvedSdkVersion` | `dotNetSdk.resolvedVersion` |
| Projeto `ResolvedSdkVersion` | `projects[].resolvedSdkVersion` |
| Projeto `DeclaredProjectSdks` | `projects[].sdks` |
| Projeto `TargetFrameworks` | `projects[].targetFrameworks` |
| Projeto `OutputType` | `projects[].outputType` |
| Projeto `IsTestProject` | `projects[].isTestProject` |
| Projeto `IsPackable` | `projects[].isPackable` |
| Projeto `RuntimeIdentifiers` | `projects[].runtimeIdentifiers` |
| Resultado do classificador automático | `projects[].classification.kind` sem override; caso contrário `projects[].classification.automaticKind` |
| Override explícito de classificação | `projects[].classification.kind` efetivo mais `projects[].classification.source` |

O dicionário bruto `Properties` do MSBuild vindo da camada de avaliação é intencionalmente excluído. Ele é uma fonte interna de evidência, não parte do contrato público estável.

Classificação, referências entre projetos e metadados Git do repositório mantêm suas formas normalizadas e são preenchidos pelos respectivos componentes da engine sem exigir tipos específicos de infraestrutura no contrato do Core.
