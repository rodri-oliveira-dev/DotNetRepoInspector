# ADR 0015: Detectar Blazor WebAssembly pelo project SDK dedicado

- **Status:** Aceito
- **Data:** 2026-09-29
- **Relacionado:** #25, #155, #174

## Contexto

O contrato de classificação suporta `classification.subtype` opcional. Investigações anteriores de subtipos Web permaneceram intencionalmente sem suporte porque o SDK comum `Microsoft.NET.Sdk.Web` é compartilhado por vários modelos de aplicação ASP.NET Core.

Blazor WebAssembly é diferente. Projetos Blazor WebAssembly standalone/client declaram explicitamente `Microsoft.NET.Sdk.BlazorWebAssembly`, que é um project SDK específico de workload, e não uma convenção inferida de arquivos-fonte, pacotes, nomes de projeto ou pastas.

O pipeline atual de inspeção já coleta as identidades dos project SDKs declarados como fatos estruturados normalizados, portanto esse sinal está disponível sem ampliar a fronteira de inspeção de código-fonte.

## Sinais considerados

| Candidato | Avaliação |
| --- | --- |
| `Microsoft.NET.Sdk.BlazorWebAssembly` declarado | Aceito. É explícito, avaliado como identidade de project SDK e específico de projetos Blazor WebAssembly standalone/client. |
| pacote `Microsoft.AspNetCore.Components.WebAssembly` | Rejeitado como sinal primário de subtipo. A presença do pacote isoladamente não estabelece o workload do projeto e pode ocorrer fora da fronteira do SDK dedicado. |
| arquivos `.razor` ou Razor SDK | Rejeitados. Fonte de componentes Razor pode aparecer em bibliotecas reutilizáveis e aplicações hospedadas no servidor. |
| nomes de projeto/pasta como `.Client` | Heurística de nome rejeitada. |
| uso de `WebAssemblyHostBuilder` no código-fonte | Rejeitado porque inspeção de código/semântica está fora do modelo atual de classificação. |

## Decisão

Reconhecer o SDK explicitamente declarado `Microsoft.NET.Sdk.BlazorWebAssembly` como subtipo Web determinístico de alta confiança:

- `classification.kind = web`;
- `classification.subtype = blazor-webassembly`;
- `classification.confidence = high`;
- sinal `sdk:Microsoft.NET.Sdk.BlazorWebAssembly`.

Sinais de projeto de teste mantêm a precedência superior existente. Se o SDK Blazor WebAssembly aparecer junto de um sinal independente de workload Worker, a classificação permanece conservadora e retorna `unknown` com sinais de conflito em vez de emitir o subtipo.

Hint de pacote, fonte de componente Razor ou Razor SDK sem o SDK dedicado de Blazor WebAssembly não devem emitir o subtipo.

## Fixtures

O conjunto de fixtures inclui:

1. `BlazorWebAssemblySubtypeSignals/StandaloneSdk`, que declara o SDK dedicado e deve emitir o subtipo suportado;
2. `BlazorWebAssemblySubtypeSignals/RazorLibraryAmbiguous`, que contém fonte de componente Razor sob `Microsoft.NET.Sdk.Razor` e deve permanecer como biblioteca sem subtipo.

## Consequências

Blazor WebAssembly passa a ser a primeira regra concreta suportada de `classification.subtype`.

O schema público não exige novo incremento de versão porque o campo opcional de subtype já foi introduzido pela #25; esta mudança passa a preencher esse campo existente quando há evidência determinística.

Blazor hospedado no servidor continua regido pela ADR 0014 e não é inferido a partir de `Microsoft.NET.Sdk.Web`, presença de componentes Razor ou convenções de fonte.
