# ADR 0010: Detectar projetos Worker além do Worker SDK declarado

**Idiomas:** [English](../../en/decisions/0010-worker-project-detection-signals.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-22
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O classificador de produção atualmente reconhece `worker` somente quando a raiz do projeto declara `Microsoft.NET.Sdk.Worker`. Essa regra é determinística, mas perde projetos com formato Worker que usam `Microsoft.NET.Sdk` comum, SDKs customizados/compostos ou integração explícita de hosting como serviço.

O Worker SDK oficial define a propriedade MSBuild avaliada `UsingMicrosoftNETSdkWorker=true` em seu `Sdk.props`. Porém, a avaliação MSBuild expõe somente o valor efetivo: o DotNetRepoInspector não consegue determinar se ele foi atribuído pelo Worker SDK, por um SDK customizado, por props importados ou pelo próprio projeto. Portanto, a propriedade é tratada como um sinal explícito de opt-in de Worker, e não como proveniência que prove a importação do Worker SDK.

A documentação de Worker da Microsoft também usa `Microsoft.Extensions.Hosting`, mas o Generic Host não é específico de Worker: consoles e aplicações Web podem usá-lo para configuração, injeção de dependência, logging, gerenciamento de lifetime e hosted services. Portanto, a presença isolada do pacote não diferencia um Worker de um console genérico com host.

Pacotes de lifetime de serviço são mais específicos. `Microsoft.Extensions.Hosting.Systemd` existe para hospedar uma aplicação .NET como serviço Linux systemd, e `Microsoft.Extensions.Hosting.WindowsServices` fornece integração de lifetime com Windows Service. Esses pacotes expressam intenção de deployment como serviço de longa duração, mas ainda podem ser usados fora do template Worker e, por isso, são sinais moderados em vez de autoritativos.

## Decisão

### Força dos sinais

| Força | Sinal estrutural | Decisão |
| --- | --- | --- |
| Forte | `Microsoft.NET.Sdk.Worker` declarado | Sinal autoritativo de Worker SDK já existente. |
| Forte | `UsingMicrosoftNETSdkWorker == true` efetivo | Opt-in explícito de Worker aprovado. O Worker SDK define essa propriedade, mas o valor efetivo não possui proveniência da atribuição; o projeto pode defini-la manualmente. A confiança alta representa o opt-in explícito, não prova de importação do Worker SDK. |
| Moderado | `OutputType == Exe` mais `Microsoft.Extensions.Hosting.Systemd` direta/avaliada | Fallback aprovado de lifetime de serviço quando não existe conflito Test/Web mais forte. |
| Moderado | `OutputType == Exe` mais `Microsoft.Extensions.Hosting.WindowsServices` direta/avaliada | Fallback aprovado de lifetime de serviço sob as mesmas restrições. |
| Fraco, apenas de apoio | presença do pacote `Microsoft.Extensions.Hosting` | Generic Host é compartilhado por Workers, consoles e outras aplicações baseadas em host; não classificar apenas por isso. |
| Fraco, apenas de apoio | `OutputType == Exe`, `IsPackable=false`, server GC, conteúdo JSON/config, hosting abstractions | Formatos comuns de runtime/build; insuficientes isoladamente ou como conjunto genérico. |
| Rejeitado | nomes de projeto/diretório e sufixo `.Worker` | Heurística de nome não estrutural. |
| Rejeitado nesta etapa da roadmap | inspeção de código por `BackgroundService`, `IHostedService`, `AddHostedService` | Semanticamente útil, mas fora da fronteira sem análise de fonte de #47/#152. |

### Por que Generic Host sozinho não basta

O template Worker usa `Microsoft.Extensions.Hosting`, porém esse pacote fornece o Generic Host do .NET. Um executável normal pode usar Generic Host apenas para DI, configuração, logging ou shutdown controlado. Promover todo executável com esse pacote para `worker` transformaria uma técnica de implementação em classificação de workload e criaria falsos positivos sistemáticos.

A fixture `HostingOnlyAmbiguous` registra essa fronteira.

### Limitação de proveniência de `UsingMicrosoftNETSdkWorker`

A propriedade escalar intencionalmente **não** é interpretada como proveniência do Worker SDK. Um projeto com SDK comum pode definir `UsingMicrosoftNETSdkWorker=true` diretamente e é indistinguível, no nível dos fatos normalizados propostos para a #152, do mesmo valor vindo de um SDK importado/composto. A #152 pode classificar esse valor efetivo como opt-in explícito de alta confiança, mas não deve relatar nem sugerir que `Microsoft.NET.Sdk.Worker` foi importado, a menos que os fatos de SDK declarado provem isso de forma independente.

Essa escolha aceita um risco limitado de falso positivo: um projeto pode definir a propriedade acidental ou deliberadamente sem se comportar como Worker. O trade-off é considerado aceitável para classificação porque o sinal é explícito, exato e específico de Worker no nome; ele não é inferido de comportamento genérico de hosting. Consumidores que precisem de proveniência de SDK devem usar a lista de SDKs declarados, e não essa propriedade.

### Precedência proposta para a issue #152

A issue #152 deve preservar todas as regras de Test da ADR 0009 antes das decisões Worker/Web e então aplicar:

1. qualquer sinal Test aprovado -> `test` com confiança/precedência já existentes;
2. Web SDK mais qualquer sinal Worker **forte** (`Microsoft.NET.Sdk.Worker` ou `UsingMicrosoftNETSdkWorker=true`) -> `unknown` por evidências conflitantes de workload;
3. `Microsoft.NET.Sdk.Web` -> `web`, confiança alta; pacotes de lifetime de serviço não sobrepõem Web;
4. `Microsoft.NET.Sdk.Worker` -> `worker`, confiança alta;
5. `UsingMicrosoftNETSdkWorker == true` -> `worker`, confiança alta como opt-in explícito, sem afirmar proveniência do Worker SDK;
6. `OutputType == Exe` mais `Microsoft.Extensions.Hosting.Systemd` ou `Microsoft.Extensions.Hosting.WindowsServices` -> `worker`, confiança média;
7. continuar com `OutputType == Exe` -> `console`, `OutputType == Library` -> `library` e depois `unknown`.

Um pacote moderado de lifetime de serviço não cria conflito Web/Worker porque aplicações ASP.NET Core também podem ser hospedadas como Windows/systemd services. O Web SDK explícito continua sendo evidência mais forte nesse formato.

### Fatos exatos para a issue #152

A implementação deve coletar ou reutilizar somente:

- nomes existentes dos project SDKs declarados;
- `OutputType` normalizado existente;
- novo `bool? UsingMicrosoftNETSdkWorker` efetivo, interpretado somente como valor de opt-in explícito e não como proveniência de importação do SDK;
- identidades normalizadas existentes de `PackageReference`, introduzidas pela #151.

Não é necessária nova coleta de items de pacote. `MsBuildProjectFactsEvaluator` já solicita `PackageReference`; a #152 precisa adicionar somente uma propriedade à avaliação existente por `dotnet msbuild -getProperty`.

Sinais estáveis sugeridos:

- `sdk:Microsoft.NET.Sdk.Worker` existente;
- `property:UsingMicrosoftNETSdkWorker=true`;
- `package:Microsoft.Extensions.Hosting.Systemd`;
- `package:Microsoft.Extensions.Hosting.WindowsServices`.

O classificador Core deve continuar recebendo somente valores normalizados e permanecer independente de MSBuild.

### Sinais avaliados e não selecionados

O Worker SDK também emite a capability de project system `DotNetCoreWorker`. Ela é evidência específica de Worker, mas um item avaliado também não prova sozinho qual import o declarou. Coletar `ProjectCapability` ainda adicionaria outro conjunto de items avaliados enquanto duplicaria em grande parte a propriedade de opt-in mais barata; por isso, não é aprovado para a #152.

`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.Abstractions`, propriedades de GC, defaults de cópia de conteúdo e pacotes genéricos de hosting foram avaliados e permanecem não autoritativos.

## Fixtures e evidências

As fixtures de pesquisa ficam em `tests/Fixtures/WorkerProjectSignals` para não alterar a baseline de smoke existente em `ProjectKinds` antes da #152:

- `UsingWorkerProperty`: executável com SDK comum que faz opt-in manual por `UsingMicrosoftNETSdkWorker=true`; comprova tanto o falso negativo atual como `console` quanto a ausência de proveniência da atribuição;
- `SystemdService`: executável com SDK comum e integração explícita com systemd; representa o fallback moderado aprovado;
- `HostingOnlyAmbiguous`: executável apenas com `Microsoft.Extensions.Hosting`; permanece `console` e demonstra por que Generic Host sozinho é insuficiente;
- `WebConflict`: Web SDK mais o flag Worker forte; registra o futuro conflito conservador como `unknown`.

`WorkerProjectSignalResearchTests` verifica que esses fatos são observáveis pela avaliação MSBuild atual e documenta o comportamento de produção vigente sem alterar o classifier.

## Custo de coleta

O custo incremental proposto para a #152 é uma propriedade MSBuild adicional, `UsingMicrosoftNETSdkWorker`, na requisição de avaliação já existente. Não é necessário processo filho adicional, restore, parsing de fonte ou etapa de resolução de pacotes.

A detecção de lifetime de serviço reutiliza os items `PackageReference` normalizados que já são coletados para classificação de Test Projects. Portanto, o custo marginal esperado de memória/CPU fica limitado a uma propriedade escalar por projeto e verificações de membership de custo constante sobre a lista de pacotes existente.

## Consequências

A implementação seguinte poderá reconhecer projetos que anunciam explicitamente semântica Worker por `UsingMicrosoftNETSdkWorker=true` com alta confiança e aplicações de serviço com SDK comum com confiança média, sem criar falsos positivos amplos por Generic Host. Essa classificação não afirma onde a propriedade foi atribuída.

Alguns Workers reais que usam somente `Microsoft.Extensions.Hosting` e registram `BackgroundService` no código continuarão intencionalmente como `console`. Detectá-los exigiria análise de fonte ou evidência semântica mais rica, fora desta etapa da roadmap.

## Alternativas consideradas

- **Classificar qualquer executável com `Microsoft.Extensions.Hosting` como Worker:** rejeitado porque Generic Host é propositalmente genérico.
- **Inspecionar C# por `BackgroundService`, `IHostedService` ou `AddHostedService`:** rejeitado nesta etapa porque introduz análise semântica de linguagem.
- **Coletar `ProjectCapability=DotNetCoreWorker`:** adiado porque duplica a propriedade oficial de Worker com custo maior de coleta.
- **Usar nomes ou sufixo `.Worker`:** rejeitado por ser não estrutural.
- **Exigir o pacote de lifetime e também o pacote Generic Host:** rejeitado como desnecessário; os pacotes de serviço já expressam o conceito de hosting e a forma de dependência direta pode variar.

## Referências

- .NET SDK Worker `Sdk.props`: https://github.com/dotnet/sdk/blob/main/src/WebSdk/Worker/Sdk/Sdk.props
- .NET SDK Worker targets: https://github.com/dotnet/sdk/blob/main/src/WebSdk/Worker/Targets/Microsoft.NET.Sdk.Worker.targets
- Microsoft Learn — background tasks com hosted services: https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services
- Microsoft Learn — Windows Service com BackgroundService: https://learn.microsoft.com/dotnet/core/extensions/windows-service
- Pacote Microsoft.Extensions.Hosting.Systemd: https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Systemd
- Issue #47: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/47
- Follow-up #152: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/152
