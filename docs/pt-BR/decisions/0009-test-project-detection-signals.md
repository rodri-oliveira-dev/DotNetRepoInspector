# ADR 0009: Detectar projetos de teste por sinais de VSTest e Microsoft.Testing.Platform

**Idiomas:** [English](../../en/decisions/0009-test-project-detection-signals.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-22
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O classificador de produção atualmente reconhece um projeto de teste somente quando a propriedade avaliada `IsTestProject` é `true`. Isso está correto para o modelo VSTest, mas não é uma definição completa de uma aplicação de teste .NET moderna.

A Microsoft documenta `IsTestProject` como a propriedade orientada a VSTest definida por `Microsoft.NET.Test.Sdk`. Microsoft.Testing.Platform (MTP) possui outra propriedade avaliada, `IsTestingPlatformApplication`, que identifica um projeto como aplicação MTP e pode ser `true` mesmo quando `IsTestProject` não é `true`.

O DotNetRepoInspector também avalia projetos sem executar restore. Nesse modo, uma `PackageReference` direta continua observável mesmo quando imports NuGet gerados, que normalmente definiriam uma propriedade MSBuild, não estão disponíveis. Assim, o classificador atual possui dois formatos reproduzíveis de falso negativo:

- uma aplicação de teste MTP com `IsTestingPlatformApplication=true`, `IsTestProject != true` e saída executável é hoje classificada como `console`;
- um projeto com referência direta a `Microsoft.NET.Test.Sdk`, mas sem `IsTestProject`, é hoje classificado apenas pelo formato genérico de saída.

O classificador deve permanecer determinístico, agnóstico de infraestrutura no Core e independente de nomes de projeto, nomes de diretório e inspeção de código-fonte.

## Decisão

### Força dos sinais

| Força | Sinal estrutural | Decisão |
| --- | --- | --- |
| Forte | `IsTestProject == true` efetivo | Sinal autoritativo de projeto de teste VSTest. |
| Forte | `IsTestingPlatformApplication == true` efetivo | Sinal autoritativo de aplicação de teste MTP. |
| Forte | project SDK declarado `MSTest.Sdk` | SDK explicitamente específico de testes; classificar como teste. |
| Moderado | `PackageReference` direta/avaliada para `Microsoft.NET.Test.Sdk` quando `IsTestProject` está ausente | Fallback de teste, pois imports do pacote podem não estar disponíveis na avaliação sem restore. |
| Moderado, apenas de apoio | `PackageReference` direta/avaliada para `Microsoft.Testing.Platform.MSBuild` | Evidência de integração MTP, mas insuficiente isoladamente porque a integração pode fluir transitivamente e consumidores não-test podem desabilitar o comportamento de aplicação. |
| Fraco, apenas de apoio | presença do pacote `Microsoft.Testing.Platform`, pacotes de framework, propriedades de seleção de runner ou seleção MTP em nível de repositório | Apenas contexto; não classificar como teste usando somente esses sinais. |
| Rejeitado | nome de projeto, nome de diretório, sufixo `.Tests`, atributos/tipos no código-fonte | Evidência heurística ou não estrutural; nunca autoritativa. |

### `false` explícito e ambiguidade

`IsTestProject=false` é um sinal negativo explícito de **VSTest**, e não uma afirmação universal de que o projeto não pode ser uma aplicação de teste.

Portanto:

- `IsTestingPlatformApplication=true` ainda pode classificar o projeto como `test` mesmo com `IsTestProject=false`;
- `MSTest.Sdk` declarado continua sendo um sinal forte de teste;
- uma referência a `Microsoft.NET.Test.Sdk` **não deve** sobrepor sozinha um `IsTestProject=false` explícito;
- presença isolada de `Microsoft.Testing.Platform.MSBuild` ou `Microsoft.Testing.Platform` nunca sobrepõe fatos negativos/ambíguos explícitos.

A fixture `ExplicitFalseConflict` registra essa fronteira conservadora.

### Precedência implementada

A issue #151 implementa a seguinte ordem antes das regras existentes de Web/Worker/Console/Library:

1. `IsTestingPlatformApplication == true` -> `test`, confiança alta.
2. `IsTestProject == true` -> `test`, confiança alta.
3. `MSTest.Sdk` declarado -> `test`, confiança alta.
4. quando `IsTestProject is null`, `Microsoft.NET.Test.Sdk` direta/avaliada -> `test`, confiança média.
5. quando `IsTestProject == false`, não promover somente pela presença do pacote `Microsoft.NET.Test.Sdk`.
6. hints de MTP baseados apenas em pacote permanecem não autoritativos.
7. continuar com a precedência atual de conflito de SDKs especializados, Web, Worker, Console, Library e Unknown.

Um sinal forte de teste continua vencendo output executável e SDKs especializados, preservando a regra atual de que a semântica de teste possui precedência sobre o formato Web/Worker/Console.

### Fatos normalizados implementados

A implementação coleta ou reutiliza estes fatos normalizados de classificação:

- `bool? IsTestProject` existente;
- novo `bool? IsTestingPlatformApplication` efetivo;
- nomes existentes dos project SDKs declarados, incluindo detecção exata/case-insensitive de `MSTest.Sdk`;
- novas identidades avaliadas de `PackageReference`, com detecção exata/case-insensitive de:
  - `Microsoft.NET.Test.Sdk`;
  - `Microsoft.Testing.Platform.MSBuild` apenas como evidência de apoio.

O adapter MSBuild é responsável por coleta/normalização. O Core recebe somente valores normalizados e permanece sem dependência de MSBuild.

Sinais estáveis sugeridos para a classificação:

- `property:IsTestProject=true`;
- `property:IsTestingPlatformApplication=true`;
- `sdk:MSTest.Sdk`;
- `package:Microsoft.NET.Test.Sdk`.

O campo público `projects[].isTestProject` deve manter o significado atual: o fato MSBuild avaliado `IsTestProject`. A implementação não redefine esse campo para representar a classificação derivada mais ampla.

## Fixtures e evidências

As fixtures de pesquisa ficam em `tests/Fixtures/TestProjectSignals` para não alterar a baseline existente de smoke de pacote/Action em `ProjectKinds` antes da #151:

- `MtpApplication` reproduz uma aplicação MTP com `IsTestingPlatformApplication=true` e `IsTestProject` ausente;
- `TestSdkFallback` expõe uma referência direta a `Microsoft.NET.Test.Sdk` com `IsTestProject` ausente;
- `ExplicitFalseConflict` combina `IsTestProject=false` com o hint do pacote e comprova a fronteira de ambiguidade.

`TestProjectSignalClassificationTests` verifica que os sinais aprovados são observáveis pela infraestrutura MSBuild atual e agora produzem as classificações de produção esperadas.

## Consequências

A implementação seguinte poderá suportar VSTest e MTP sem heurísticas de nome e sem tornar o Core dependente de MSBuild. Também preserva a distinção entre uma propriedade MSBuild observada e uma classificação derivada.

Coletar itens `PackageReference` adiciona uma quantidade limitada de dados à avaliação. O fallback é intencionalmente mais estreito que "qualquer pacote de framework de testes", reduzindo falsos positivos em bibliotecas auxiliares ou projetos que apenas usam bibliotecas de teste.

A issue #151 converteu as expectativas de pesquisa em asserções da classificação de produção, preservando a fixture de ambiguidade.

## Alternativas consideradas

- **Usar nomes de projeto/diretório como `.Tests`:** rejeitado por ser não estrutural e propenso a classificação incorreta.
- **Tratar qualquer pacote de framework de teste como autoritativo:** rejeitado porque pacotes de assertion/framework podem existir em bibliotecas auxiliares.
- **Tratar apenas a presença de `Microsoft.Testing.Platform.MSBuild` como autoritativa:** rejeitado porque a integração MTP pode ser transitiva e desabilitada em consumidores não-test.
- **Redefinir o `isTestProject` público para o resultado derivado mais amplo:** rejeitado porque misturaria um fato MSBuild observado com a saída do classificador.
- **Executar restore antes da classificação:** rejeitado nesta issue porque altera a fronteira de side effects/performance da inspeção e é desnecessário quando há fatos estruturais de fallback.

## Referências

- Microsoft Learn — propriedades MSBuild para Microsoft.NET.Sdk: https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props
- Microsoft Learn — visão geral do Microsoft.Testing.Platform: https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro
- Issue #96: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/96
- Implementação #151: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/151
