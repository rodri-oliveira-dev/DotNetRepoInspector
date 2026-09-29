# ADR 0013: Manter o subtipo Razor Pages sem suporte na ausência de evidência estrutural determinística

- **Status:** Aceito
- **Data:** 2026-09-29
- **Relacionado:** #25, #153, #154, #173

## Contexto

O contrato de classificação suporta `classification.subtype` opcional, enquanto projetos ASP.NET Core já recebem a classificação base `web` a partir de `Microsoft.NET.Sdk.Web`.

A issue #173 avalia se o modelo de inspeção atual consegue identificar Razor Pages deterministicamente sem depender de convenções de nome/pasta ou análise ampla de código/semântica.

Razor Pages e MVC Views compartilham o pipeline do Razor SDK. Na camada MSBuild, ambos são representados como entradas Razor `.cshtml`, e o Web SDK habilita o mesmo suporte de build MVC/Razor para os dois modelos de aplicação.

## Sinais considerados

| Candidato | Avaliação |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Autoritativo apenas para a classificação Web base; compartilhado pelos principais modelos Web do ASP.NET Core. |
| `AddRazorSupportForMvc == true` | Dá suporte explicitamente a aplicações contendo MVC Views ou Razor Pages e é habilitado implicitamente por projetos Web SDK modernos. |
| itens `RazorGenerate` avaliados | Representam entradas Razor `.cshtml` tanto de MVC Views quanto de Razor Pages; o tipo de item não carrega distinção semântica entre página e view. |
| `Microsoft.NET.Sdk.Razor` | Comprova capacidade de build Razor e também é usado por Razor Class Libraries. |
| `Pages/**`, `.cshtml.cs` ou nomes de projeto/pasta | Heurísticas de convenção/caminho/nome, não evidência autoritativa do modelo de aplicação. |
| `PageModel`, `AddRazorPages` ou `MapRazorPages` | Exigem inspeção C#/semântica e podem coexistir com MVC, APIs ou outros modelos Web. |
| diretiva Razor `@page` | É o marcador que distingue uma Razor Page, mas detectá-lo exige leitura do conteúdo-fonte Razor, algo que o modelo atual de classificação não faz. |

Nenhum fato de projeto/MSBuild atualmente normalizado é ao mesmo tempo necessário e suficiente para identificar o modelo Razor Pages.

## Decisão

Não introduzir regra de subtipo Razor Pages.

Para projetos Web:

- manter `classification.kind = web`;
- preservar confiança e sinais existentes da classificação Web base;
- manter `classification.subtype` ausente;
- não inferir Razor Pages a partir de `AddRazorSupportForMvc`, `RazorGenerate`, Razor SDK, caminhos do sistema de arquivos ou convenções de nome.

As fixtures em `tests/Fixtures/RazorPagesSubtypeSignals` registram a fronteira de ambiguidade:

1. um projeto Web SDK contém uma Razor Page real (com `@page`) e uma MVC Razor View comum, enquanto o MSBuild expõe ambas pelo mesmo tipo de item `RazorGenerate`;
2. uma Razor Class Library pode expor `RazorGenerate` mais `AddRazorSupportForMvc=true` e continuar sendo `library`, demonstrando que esses fatos não implicam um subtipo Web Razor Pages.

Os testes de pesquisa inspecionam o conteúdo das fixtures apenas para estabelecer o ground truth. A classificação permanece baseada exclusivamente nos fatos estruturados existentes e, portanto, não emite subtipo.

## Consequências

O inspetor permanece deliberadamente menos específico em vez de tratar metadados genéricos de build Razor ou convenções de caminho como evidência de Razor Pages.

Uma ADR futura poderá substituir esta decisão se inspeção limitada de fonte Razor for explicitamente aprovada ou se surgir um novo sinal estruturado autoritativo. Qualquer regra futura deverá preservar `kind = web`, definir precedência com outros subtipos Web e manter cobertura explícita para casos ambíguos/non-match.
