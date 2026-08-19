# Classificação de projetos

**Idiomas:** [English](../en/classification.md) | Português (Brasil)

O DotNetRepoInspector classifica projetos a partir de fatos estruturais avaliados, em vez de usar nomes de projetos, nomes de diretórios ou inspeção do código-fonte.

As classificações iniciais são `web`, `worker`, `console`, `library`, `test` e `unknown`.

## Entradas

Atualmente, o classificador consome apenas fatos que já foram normalizados pelo pipeline de inspeção:

- nomes dos SDKs declarados pelo projeto;
- `OutputType` efetivo;
- `IsTestProject` efetivo.

O classificador do Core não possui dependência de MSBuild. `MsBuildProjectClassificationAdapter` converte `MsBuildProjectFacts` para o modelo de entrada do Core.

## Precedência e tratamento de conflitos

As regras são avaliadas nesta ordem:

1. `IsTestProject == true` -> `test`.
2. presença simultânea de `Microsoft.NET.Sdk.Web` e `Microsoft.NET.Sdk.Worker` -> `unknown`, pois os sinais de SDK especializados entram em conflito.
3. `Microsoft.NET.Sdk.Web` -> `web`.
4. `Microsoft.NET.Sdk.Worker` -> `worker`.
5. `OutputType == Exe` -> `console` quando nenhum SDK especializado correspondeu.
6. `OutputType == Library` -> `library` quando nenhum SDK especializado correspondeu.
7. caso contrário -> `unknown`.

Portanto, um projeto de teste permanece `test` mesmo que seja executável ou declare um SDK especializado. Declarações conflitantes dos SDKs Web/Worker produzem intencionalmente `unknown`, em vez de depender de uma ordenação arbitrária.

`WinExe` não é classificado como `console`, pois pode representar modelos de aplicação desktop que estão fora do vocabulário inicial de classificação.

## Sinais e confiança

| Classificação | Evidência estrutural | Sinal | Confiança |
| --- | --- | --- | --- |
| `test` | `IsTestProject == true` | `property:IsTestProject=true` | `high` |
| `web` | `Microsoft.NET.Sdk.Web` declarado | `sdk:Microsoft.NET.Sdk.Web` | `high` |
| `worker` | `Microsoft.NET.Sdk.Worker` declarado | `sdk:Microsoft.NET.Sdk.Worker` | `high` |
| `console` | `OutputType == Exe` efetivo | `property:OutputType=Exe` | `medium` |
| `library` | `OutputType == Library` efetivo | `property:OutputType=Library` | `high` |
| `unknown` | evidência insuficiente ou conflitante | fatos observados/sinal de conflito quando disponível | omitido |

## Heurísticas deliberadamente excluídas

O engine inicial não classifica com base em:

- sufixos como `.Api`, `.Worker` ou `.Tests`;
- nomes de projeto ou diretório;
- apenas a presença de `Microsoft.Extensions.Hosting`;
- presença de `BackgroundService` ou de outros tipos no código-fonte;
- propriedades MSBuild arbitrárias e brutas que não tenham sido promovidas a fatos normalizados de classificação.

Esses sinais só devem ser considerados no futuro se o modelo de inspeção passar a coletá-los explicitamente e se a regra puder ser documentada com precedência determinística.
