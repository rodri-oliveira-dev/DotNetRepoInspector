# ADR 0012: Manter o subtipo MVC sem suporte na ausência de evidência estrutural determinística

- **Status:** Aceito
- **Data:** 2026-09-29
- **Relacionado:** #25, #153, #154

## Contexto

O contrato de classificação suporta `classification.subtype` opcional, e projetos Web já são identificados a partir do SDK `Microsoft.NET.Sdk.Web` declarado.

A issue #154 avalia se o modelo de inspeção atual consegue refinar `kind = web` para um subtipo MVC sem heurísticas por nome de projeto/diretório, inspeção de código-fonte ou análise semântica ampla.

MVC é particularmente fácil de detectar em excesso porque o ASP.NET Core compartilha infraestrutura Razor e MVC entre MVC Views, Razor Pages, APIs, Razor Class Libraries e aplicações mistas.

## Sinais considerados

| Candidato | Avaliação |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Autoritativo apenas para a classificação Web base; é compartilhado pelos modelos de aplicação ASP.NET Core. |
| `AddRazorSupportForMvc == true` | Não é específico de MVC. O Razor SDK o usa para MVC Views ou Razor Pages, e projetos Web SDK modernos o definem implicitamente. |
| `Microsoft.NET.Sdk.Razor` mais `AddRazorSupportForMvc == true` | Pode representar uma Razor Class Library em vez de uma aplicação Web. |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | Capacidade opcional de compilação Razor em runtime e não comprova uso de MVC com controllers/views. |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | Infraestrutura MVC utilizável em cenários de controllers/API e não comprova MVC baseado em views. |
| `Views/**`, `Controllers/**`, nomes de projeto ou pastas | Heurísticas de convenção por caminho/nome e compatíveis com aplicações mistas. |
| herança de controller, `AddControllersWithViews`, rotas convencionais ou actions que retornam views | Potencialmente significativos apenas por análise de código/semântica, fora do escopo. |

Nenhum fato de projeto/MSBuild avaliado disponível é ao mesmo tempo necessário e suficiente para o modelo MVC de controllers/views.

## Decisão

Não introduzir regra de subtipo MVC.

Para projetos Web:

- manter `classification.kind = web`;
- preservar confiança e sinais existentes da classificação Web base;
- manter `classification.subtype` ausente;
- não promover `AddRazorSupportForMvc`, uso do Razor SDK, pacotes MVC/Razor, nomes, pastas ou tooling opcional a evidência de subtipo.

As fixtures em `tests/Fixtures/MvcSubtypeSignals` registram duas fronteiras importantes de ambiguidade:

1. um projeto Web SDK moderno avalia `AddRazorSupportForMvc=true` implicitamente e continua sendo apenas `web` base;
2. uma Razor Class Library pode definir explicitamente a mesma propriedade e continuar sendo `library`, demonstrando que a propriedade não comprova uma aplicação Web MVC.

Testes de regressão no Core também garantem que hints de pacotes comuns MVC/Razor não preencham subtipo.

## Consequências

O inspetor permanece deliberadamente menos específico em vez de emitir um rótulo MVC frágil. Consumidores podem confiar na classificação Web base sem interpretar infraestrutura Razor/MVC compartilhada como prova do modelo de aplicação.

Uma ADR futura poderá substituir esta decisão se o modelo de inspeção ganhar um fato estrutural MVC autoritativo ou se análise semântica limitada passar a ser uma entrada explicitamente suportada. Qualquer regra desse tipo deverá preservar `kind = web`, definir precedência determinística com outros subtipos Web e incluir fixtures positivas e ambíguas.
