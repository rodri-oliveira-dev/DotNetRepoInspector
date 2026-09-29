# Classificação de projetos

**Idiomas:** [English](../en/classification.md) | Português (Brasil)

O DotNetRepoInspector classifica projetos a partir de fatos estruturais avaliados, em vez de usar nomes de projetos, nomes de diretórios ou inspeção do código-fonte.

As classificações são `web`, `worker`, `console`, `library`, `test` e `unknown`.

`projects[].classification.subtype` é um refinamento opcional separado do tipo base de classificação. O classificador atual não preenche subtipos concretos; o campo permanece ausente até que uma regra futura aprovada forneça evidência explícita de subtipo.

## Subtipo Web API: não suportado intencionalmente

Web API **não** é emitido atualmente como subtipo. O modelo de inspeção não expõe um fato estrutural no nível de projeto que diferencie de forma única projetos ASP.NET Core Web API de MVC, Razor Pages, Blazor ou aplicações Web mistas.

A decisão está documentada na [ADR 0011](decisions/0011-web-api-subtype-detection.md). Os candidatos avaliados são rejeitados intencionalmente como regras de subtipo:

| Evidência candidata | Por que é insuficiente |
| --- | --- |
| `Microsoft.NET.Sdk.Web` declarado | É autoritativo para o `kind = web` base, mas é compartilhado por vários modelos de aplicação ASP.NET Core. |
| `OutputType == Exe` efetivo | É comum aos hosts ASP.NET Core modernos e não é específico de API. |
| pacotes como `Microsoft.AspNetCore.OpenApi` ou `Swashbuckle.AspNetCore` | São tooling opcional para API, podem ser removidos de APIs válidas e podem ser usados por aplicações Web mistas/não exclusivamente API. |
| propriedades/itens relacionados a Razor | Podem comprovar suporte a Razor, mas não comprovam ausência de endpoints de API; modelos de aplicação podem coexistir. |
| perfis de launch, como URL de launch para `swagger` | São configuração opcional de tooling fora dos fatos normalizados e avaliados de classificação. |
| marcadores de código-fonte como `MapGet`, `[ApiController]` ou `ControllerBase` | Exigiriam análise de código/semântica, fora da fronteira de subtipo desta issue. |

Consequentemente, um projeto classificado como `web` mantém `classification.subtype` ausente mesmo quando possui hints de pacotes comuns em APIs. `classification.confidence` continua descrevendo a classificação base `web`; nenhuma confiança de subtipo é inventada.

## Subtipo MVC: não suportado intencionalmente

MVC **não** é emitido atualmente como subtipo. Os fatos de projeto/MSBuild avaliados atualmente podem comprovar capacidade Web ou Razor, mas não comprovam que a aplicação realmente usa o modelo MVC de controllers/views em vez de Razor Pages, endpoints de API, Blazor, uma Razor Class Library ou um modelo misto.

A decisão está documentada na [ADR 0012](decisions/0012-mvc-subtype-detection.md). Os principais candidatos são rejeitados intencionalmente como regras de subtipo:

| Evidência candidata | Por que é insuficiente |
| --- | --- |
| `Microsoft.NET.Sdk.Web` declarado | Comprova apenas o workload `web` base e é compartilhado pelos principais modelos de aplicação Web do ASP.NET Core. |
| `AddRazorSupportForMvc == true` efetivo | O Razor SDK o usa para MVC Views **ou Razor Pages**, e projetos Web SDK no .NET moderno o definem implicitamente. |
| `Microsoft.NET.Sdk.Razor` declarado mais `AddRazorSupportForMvc == true` | Também representa Razor Class Libraries e, portanto, não comprova uma aplicação Web MVC. |
| pacotes MVC/Razor, como runtime compilation ou integração JSON | São capacidades opcionais e infraestrutura MVC transversal; não estabelecem uso de controllers/views. |
| `Views/**`, `Controllers/**` ou nomes de projeto | Heurísticas de convenção/caminho/nome; aplicações mistas continuam válidas e esses sinais não são autoritativos. |
| herança de controller, `AddControllersWithViews`, rotas ou actions que retornam views | Exigiriam análise de código ou semântica, fora da fronteira de subtipo suportada. |

Portanto, um projeto com `kind = web` mantém `classification.subtype` ausente mesmo quando há hints orientados a Razor/MVC. Um futuro subtipo MVC exige um fato estrutural autoritativo ou um modelo de análise semântica limitada explicitamente aprovado.

## Subtipo Razor Pages: não suportado intencionalmente

Razor Pages **não** é emitido atualmente como subtipo. O modelo atual de projeto/MSBuild consegue identificar capacidades de build Web e Razor, mas não distingue uma Razor Page de uma MVC Razor View sem inspecionar o conteúdo-fonte Razor.

A decisão está documentada na [ADR 0013](decisions/0013-razor-pages-subtype-detection.md). Os candidatos avaliados são rejeitados intencionalmente como regras de subtipo:

| Evidência candidata | Por que é insuficiente |
| --- | --- |
| `Microsoft.NET.Sdk.Web` declarado | Comprova apenas o workload `web` base e é compartilhado por MVC, Razor Pages, APIs, Blazor e aplicações mistas. |
| `AddRazorSupportForMvc == true` efetivo | O Razor SDK o usa para aplicações contendo MVC Views **ou Razor Pages**, e projetos Web SDK modernos o definem implicitamente. |
| `RazorGenerate` / entradas de build `.cshtml` | A fronteira atual de avaliação de propriedades/itens não expõe os itens `RazorGenerate` padrão sem execução adicional de targets; mesmo entradas Razor genéricas não carregariam a distinção semântica da diretiva `@page`. |
| `Microsoft.NET.Sdk.Razor` declarado | Comprova capacidade de build Razor, incluindo Razor Class Libraries, não uma aplicação Web Razor Pages. |
| caminho `Pages/**` ou arquivo companheiro `.cshtml.cs` | Heurística de convenção/caminho do sistema de arquivos e explicitamente fora da fronteira aprovada de subtipo. |
| `PageModel`, `AddRazorPages` ou `MapRazorPages` | Exigem inspeção de código/semântica e podem coexistir com outros modelos de aplicação ASP.NET Core. |
| diretiva Razor `@page` | É o marcador que distingue Razor Pages, mas detectá-lo exige leitura do conteúdo-fonte Razor, fora do modelo de inspeção atual. |

Portanto, um projeto com `kind = web` mantém `classification.subtype` ausente mesmo quando existe fonte Razor. Um futuro subtipo Razor Pages exige uma fronteira aprovada de inspeção de conteúdo/semântica ou um novo sinal estruturado autoritativo.

## Entradas

O classificador consome fatos normalizados produzidos pelo pipeline de inspeção:

- nomes dos project SDKs declarados;
- `OutputType` efetivo;
- `IsTestProject` efetivo;
- `IsTestingPlatformApplication` efetivo;
- `UsingMicrosoftNETSdkWorker` efetivo;
- identidades avaliadas de `PackageReference` usadas pelas regras de classificação aprovadas.

O classificador do Core não possui dependência de MSBuild. `MsBuildProjectClassificationAdapter` converte `MsBuildProjectFacts` para o modelo de entrada do Core.

Para projetos multi-target, os fatos de classificação são avaliados em cada inner build do MSBuild. Propriedades de Worker e referências de pacote são combinadas entre os target frameworks, enquanto um pacote de lifetime de serviço só é associado a `OutputType == Exe` quando ambos os fatos ocorrem no mesmo target framework.

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
5. `Microsoft.NET.Sdk.Web` mais um sinal Worker forte (`Microsoft.NET.Sdk.Worker` ou `UsingMicrosoftNETSdkWorker == true`) -> `unknown`, pois os sinais de workload entram em conflito.
6. `Microsoft.NET.Sdk.Web` -> `web`; hints de pacote de lifetime de serviço não sobrepõem Web.
7. `Microsoft.NET.Sdk.Worker` -> `worker`.
8. `UsingMicrosoftNETSdkWorker == true` -> `worker`.
9. `OutputType == Exe` mais `Microsoft.Extensions.Hosting.Systemd` ou `Microsoft.Extensions.Hosting.WindowsServices` -> `worker`.
10. `OutputType == Exe` -> `console` quando nenhum sinal mais específico correspondeu.
11. `OutputType == Library` -> `library` quando nenhum sinal mais específico correspondeu.
12. caso contrário -> `unknown`.

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
| `worker` | propriedade de opt-in explícito de Worker | `property:UsingMicrosoftNETSdkWorker=true` | `high` |
| `worker` | executável com integração de lifetime systemd | `package:Microsoft.Extensions.Hosting.Systemd` | `medium` |
| `worker` | executável com integração de lifetime Windows Service | `package:Microsoft.Extensions.Hosting.WindowsServices` | `medium` |
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

Regras de subtipo seguem o mesmo critério: elas devem usar metadados avaliados e aprovados, evitar inspeção de código-fonte e heurísticas por nome/caminho, e preservar a semântica do `classification.kind` base.
