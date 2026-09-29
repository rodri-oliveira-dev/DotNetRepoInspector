# ADR 0011: Manter o subtipo Web API sem suporte na ausência de evidência estrutural determinística

- **Status:** Aceito
- **Data:** 2026-09-28
- **Relacionado:** #25, #153

## Contexto

O contrato de classificação agora suporta `classification.subtype` opcional, enquanto o classificador base existente já identifica projetos Web a partir do project SDK `Microsoft.NET.Sdk.Web` declarado.

A issue #153 avalia se o modelo de inspeção atual consegue refinar `kind = web` para um subtipo Web API sem heurísticas por nome de projeto/diretório, inspeção de código-fonte ou análise semântica ampla.

O subtipo precisa ser reproduzível a partir de fatos estruturados e não pode transformar convenções opcionais em evidência autoritativa.

## Sinais considerados

| Candidato | Avaliação |
| --- | --- |
| `Microsoft.NET.Sdk.Web` | Evidência forte apenas para a classificação Web base. MVC, Razor Pages, Blazor/hosts server-side, APIs e aplicações mistas podem compartilhá-lo. |
| `OutputType == Exe` | Forma comum de host, não específica de API. |
| `Microsoft.AspNetCore.OpenApi` | Tooling opcional orientado a API. Uma API válida pode não usá-lo e uma aplicação Web mista pode incluí-lo. |
| `Swashbuckle.AspNetCore` ou tooling Swagger similar | Convenção opcional de documentação/tooling, não um contrato do modelo de aplicação. |
| propriedades/itens Razor | Podem demonstrar capacidade Razor, mas não comprovam ausência de endpoints de API. Aplicações mistas são válidas. |
| `launchSettings.json` com valores relacionados a Swagger | Configuração opcional de tooling de desenvolvimento, não um fato de projeto normalizado e avaliado. |
| `MapGet`, `MapPost`, `[ApiController]`, `ControllerBase` | Potencialmente significativos apenas via análise de código/semântica, que está fora do escopo. |
| nomes de projeto/diretório como `.Api` | Heurística explicitamente proibida. |

Nenhum candidato é ao mesmo tempo necessário e suficiente para o modelo de aplicação Web API dentro da fronteira atual de fatos de projeto/MSBuild.

## Decisão

Não introduzir regra de subtipo Web API.

Para projetos classificados a partir de `Microsoft.NET.Sdk.Web`:

- manter `classification.kind = web`;
- manter a confiança `high` existente e o sinal base `sdk:Microsoft.NET.Sdk.Web`;
- manter `classification.subtype` ausente;
- não promover presença de pacotes OpenAPI/Swagger, presença/ausência de Razor, launch profiles, nomes ou caminhos a heurísticas de subtipo.

As fixtures de pesquisa em `tests/Fixtures/WebApiSubtypeSignals` registram tanto a forma ambígua Web SDK executável quanto o fato de que suporte a Razor pode coexistir com aplicações Web capazes de expor APIs. Testes de regressão no Core também garantem que hints de pacotes comuns de tooling de API não preencham subtipo.

## Consequências

O inspetor produz intencionalmente uma saída menos específica em vez de um rótulo Web API frágil. Consumidores podem confiar em `kind = web` sem confundir convenções com uma garantia do modelo de aplicação.

Uma ADR futura poderá substituir esta decisão se o modelo de inspeção ganhar um fato estrutural autoritativo ou se análise semântica limitada passar a ser uma entrada de classificação explicitamente suportada. Essa mudança deverá preservar a compatibilidade do tipo base e incluir fixtures positivas e negativas determinísticas.
