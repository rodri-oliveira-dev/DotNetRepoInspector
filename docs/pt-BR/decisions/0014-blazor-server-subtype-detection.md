# ADR 0014: Manter os subtipos Blazor Web App e server-side sem suporte na ausência de evidência estrutural determinística

- **Status:** Aceito
- **Data:** 2026-09-29
- **Relacionado:** #25, #153, #154, #173, #155

## Contexto

O contrato de classificação suporta `classification.subtype` opcional, enquanto aplicações ASP.NET Core hospedadas no servidor já recebem a classificação base `web` a partir de `Microsoft.NET.Sdk.Web`.

A issue #155 avalia duas variantes relacionadas: Blazor Web App moderno e hosting Blazor server-side (Interactive Server e Blazor Server clássico). O objetivo é determinar se os fatos estruturados atuais de projeto/MSBuild conseguem identificar alguma dessas variantes sem análise de código-fonte ou heurísticas de nome/caminho.

O Blazor Web App moderno usa o Web SDK padrão do ASP.NET Core. Interactive Server é habilitado por código da aplicação, como `AddInteractiveServerComponents` e `AddInteractiveServerRenderMode`. O Blazor Server clássico também usa o Web SDK e configura Blazor server-side no código da aplicação.

## Sinais considerados

| Candidato | Avaliação |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Autoritativo apenas para a classificação Web base; compartilhado por Blazor, MVC, Razor Pages, APIs e apps mistas. |
| arquivos `.razor` expostos como `Content` | Comprovam existência de fonte de componentes Razor, mas componentes podem existir em apps ASP.NET Core mistas e Razor Class Libraries. |
| `RazorComponent` | É criado por targets do Razor SDK a partir do conteúdo `.razor` após a fronteira básica de avaliação; compilação de componentes não identifica hosting/render mode. |
| framework reference implícita `Microsoft.AspNetCore.App` | Compartilhada por projetos Web ASP.NET Core e não específica de Blazor. |
| `AddRazorComponents` / `MapRazorComponents` | Evidência forte de app de Razor Components, mas disponível apenas por inspeção de código/semântica. |
| `AddInteractiveServerComponents` / `AddInteractiveServerRenderMode` | Evidência forte de hosting Interactive Server moderno, mas disponível apenas por inspeção de código/semântica. |
| `AddServerSideBlazor` / `MapBlazorHub` | Evidência forte de hosting Blazor Server clássico, mas disponível apenas por inspeção de código/semântica. |
| `Components/**`, `App.razor`, `Routes.razor`, `_Host.cshtml` | Convenções de template/caminho, não metadados autoritativos de projeto. |

Blazor WebAssembly é intencionalmente excluído desta ADR e será avaliado na #174 porque o projeto cliente autônomo possui uma fronteira de SDK distinta.

## Decisão

Não introduzir subtipo Blazor Web App nem subtipo Blazor Server/server-side.

Para as duas variantes:

- manter `classification.kind = web`;
- preservar a confiança e o sinal existentes da classificação Web base;
- manter `classification.subtype` ausente;
- não inferir hosting pela presença de arquivos `.razor`, itens de targets do Razor SDK, framework references, caminhos de template ou nomes.

As fixtures em `tests/Fixtures/BlazorServerSubtypeSignals` estabelecem o ground truth por arquivos-fonte que contêm as APIs de hosting relevantes. Os testes de pesquisa MSBuild comparam deliberadamente esse ground truth com a fronteira estrutural de avaliação: fonte `.razor` é observável como `Content` genérico, enquanto `RazorComponent` não é materializado pela avaliação básica e o classificador não recebe nenhum fato de hosting mode.

## Consequências

O inspetor permanece conservador: um projeto Web pode conter componentes Razor e até ser de fato um Blazor Web App ou app Interactive Server sem receber subtipo.

Uma ADR futura poderá substituir esta decisão se inspeção limitada de código/semântica passar a ser uma entrada aprovada de classificação ou se o SDK introduzir uma propriedade autoritativa de hosting mode. Qualquer regra futura deverá preservar `kind = web`, definir precedência com outros subtipos Web e manter explícita a ambiguidade de apps mistas.