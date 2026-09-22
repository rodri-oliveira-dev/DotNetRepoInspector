# Classificação de projetos

**Idiomas:** [English](../en/classification.md) | Português (Brasil)

O DotNetRepoInspector classifica projetos a partir de fatos estruturais avaliados, em vez de usar nomes de projetos, nomes de diretórios ou inspeção do código-fonte.

As classificações são `web`, `worker`, `console`, `library`, `test` e `unknown`.

## Entradas

O classificador consome fatos normalizados produzidos pelo pipeline de inspeção:

- nomes dos project SDKs declarados;
- `OutputType` efetivo;
- `IsTestProject` efetivo;
- `IsTestingPlatformApplication` efetivo;
- identidades avaliadas de `PackageReference` usadas pelas regras de classificação aprovadas.

O classificador do Core não possui dependência de MSBuild. `MsBuildProjectClassificationAdapter` converte `MsBuildProjectFacts` para o modelo de entrada do Core.

O campo público `projects[].isTestProject` mantém o significado original: ele representa o fato MSBuild avaliado `IsTestProject`. Assim, um projeto pode ser classificado como `test` por outro sinal aprovado enquanto `isTestProject` é `false` ou está ausente.

## Sinais de projeto de teste

A detecção de projetos de teste segue a [ADR 0009](decisions/0009-test-project-detection-signals.md):

| Evidência | Sinal | Confiança | Observação |
| --- | --- | --- | --- |
| `IsTestingPlatformApplication == true` | `property:IsTestingPlatformApplication=true` | `high` | Sinal autoritativo de aplicação Microsoft.Testing.Platform. |
| `IsTestProject == true` | `property:IsTestProject=true` | `high` | Sinal autoritativo VSTest já existente. |
| `MSTest.Sdk` declarado | `sdk:MSTest.Sdk` | `high` | Project SDK explicitamente específico de testes. |
| pacote `Microsoft.NET.Test.Sdk` avaliado com `IsTestProject` ausente | `package:Microsoft.NET.Test.Sdk` | `medium` | Fallback conservador para lacunas de imports de pacote em avaliação sem restore. |

Um `IsTestProject=false` explícito impede que apenas o fallback de pacote `Microsoft.NET.Test.Sdk` promova o projeto para `test`. Ele não anula sinais fortes independentes, como `IsTestingPlatformApplication=true` ou `MSTest.Sdk` declarado.

Hints MTP baseados apenas em pacote, como `Microsoft.Testing.Platform.MSBuild`, pacotes genéricos de framework de testes, seleção de runner no repositório, nomes e caminhos não são autoritativos isoladamente.

## Precedência e tratamento de conflitos

As regras são avaliadas nesta ordem:

1. `IsTestingPlatformApplication == true` -> `test`.
2. `IsTestProject == true` -> `test`.
3. `MSTest.Sdk` declarado -> `test`.
4. quando `IsTestProject` está ausente, `Microsoft.NET.Test.Sdk` avaliado -> `test`.
5. presença simultânea de `Microsoft.NET.Sdk.Web` e `Microsoft.NET.Sdk.Worker` -> `unknown`, pois os sinais de SDK especializados entram em conflito.
6. `Microsoft.NET.Sdk.Web` -> `web`.
7. `Microsoft.NET.Sdk.Worker` -> `worker`.
8. `OutputType == Exe` -> `console` quando nenhum sinal mais específico correspondeu.
9. `OutputType == Library` -> `library` quando nenhum sinal mais específico correspondeu.
10. caso contrário -> `unknown`.

Portanto, um sinal forte de teste permanece `test` mesmo quando o projeto é executável ou declara um SDK de workload especializado. Declarações conflitantes dos SDKs Web/Worker produzem `unknown` somente quando nenhum sinal aprovado de teste já correspondeu.

`WinExe` não é classificado como `console`, pois pode representar modelos de aplicação desktop fora do vocabulário atual de classificação.

## Sinais e confiança

| Classificação | Evidência estrutural | Sinal | Confiança |
| --- | --- | --- | --- |
| `test` | aplicação MTP | `property:IsTestingPlatformApplication=true` | `high` |
| `test` | `IsTestProject == true` | `property:IsTestProject=true` | `high` |
| `test` | `MSTest.Sdk` declarado | `sdk:MSTest.Sdk` | `high` |
| `test` | fallback `Microsoft.NET.Test.Sdk` com `IsTestProject` ausente | `package:Microsoft.NET.Test.Sdk` | `medium` |
| `web` | `Microsoft.NET.Sdk.Web` declarado | `sdk:Microsoft.NET.Sdk.Web` | `high` |
| `worker` | `Microsoft.NET.Sdk.Worker` declarado | `sdk:Microsoft.NET.Sdk.Worker` | `high` |
| `console` | `OutputType == Exe` efetivo | `property:OutputType=Exe` | `medium` |
| `library` | `OutputType == Library` efetivo | `property:OutputType=Library` | `high` |
| `unknown` | evidência insuficiente ou conflitante | fatos observados/sinal de conflito quando disponível | omitido |

## Heurísticas deliberadamente excluídas

O engine não classifica com base em:

- sufixos como `.Api`, `.Worker` ou `.Tests`;
- nomes de projeto ou diretório;
- apenas a presença de `Microsoft.Extensions.Hosting`;
- apenas a presença de `Microsoft.Testing.Platform.MSBuild` ou de pacotes genéricos de framework de testes;
- apenas a seleção de test runner no repositório;
- presença de `BackgroundService`, atributos de teste ou outros tipos no código-fonte;
- propriedades MSBuild arbitrárias e brutas que não tenham sido promovidas a fatos normalizados de classificação.

Novos sinais só devem ser adicionados quando o modelo de inspeção puder coletá-los explicitamente e sua precedência for determinística.


## Evolução aprovada dos sinais de Worker

A issue #47 pesquisa falsos negativos de Worker sem alterar as regras de produção acima. A [ADR 0010](decisions/0010-worker-project-detection-signals.md) define os fatos e a precedência que a issue #152 deve implementar.

A direção aprovada é intencionalmente mais restrita que detectar Generic Host:

- `UsingMicrosoftNETSdkWorker == true` efetivo é um sinal de **opt-in explícito** de Worker com alta confiança. O Worker SDK o define, mas o valor efetivo não possui proveniência da atribuição e também pode ser definido manualmente; a classificação não deve sugerir que o Worker SDK foi importado;
- projetos executáveis com `Microsoft.Extensions.Hosting.Systemd` ou `Microsoft.Extensions.Hosting.WindowsServices` são candidatos Worker de confiança média quando não existe evidência Test/Web mais forte;
- `Microsoft.Extensions.Hosting` isoladamente permanece apenas evidência de apoio, pois consoles comuns e aplicações Web também podem usar Generic Host;
- Web SDK mais um sinal Worker forte representa evidência conflitante de workload e deve resultar em `unknown`;
- sinais Test mantêm a precedência superior já existente;
- nomes, caminhos, sufixos `.Worker` e análise de código-fonte não são aprovados.

Até o merge da #152, a classificação de produção continua intencionalmente usando somente a regra atual baseada no Worker SDK.
