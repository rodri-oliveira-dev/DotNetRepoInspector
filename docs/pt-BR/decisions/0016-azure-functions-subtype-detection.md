# ADR 0016: Detectar Azure Functions por SDK oficial e sinais do modelo de runtime

- **Status:** Aceito
- **Data:** 2026-09-29
- **Relacionado:** #25, #156

## Contexto

Azure Functions possui atualmente dois modelos .NET relevantes:

1. **isolated worker**, no qual o código das funções executa em um processo worker .NET separado;
2. **in-process**, no qual o código executa dentro do processo host do Functions.

Na data desta decisão, a Microsoft recomenda o project SDK dedicado `Azure.Functions.Sdk` para projetos isolated worker suportados. A forma anterior de projeto isolated continua relevante durante a migração e usa `Microsoft.NET.Sdk`, `AzureFunctionsVersion`, `OutputType=Exe`, `Microsoft.Azure.Functions.Worker` e `Microsoft.Azure.Functions.Worker.Sdk`.

O modelo in-process permanece suportado até 10 de novembro de 2026. Sua forma de projeto .NET usa `AzureFunctionsVersion` e o package oficial de build `Microsoft.NET.Sdk.Functions`, com semântica de saída de biblioteca.

O inspetor já coleta project SDKs declarados e referências de pacote avaliadas. Esta ADR promove `AzureFunctionsVersion` a um fato normalizado explícito de classificação para que a detecção não dependa de acesso a propriedades brutas arbitrárias.

## Decisões por modelo

### Isolated worker — modelo atual por SDK

Suportado deterministicamente.

Evidência necessária:

- project SDK declarado `Azure.Functions.Sdk`.

Classificação:

- `classification.kind = worker`;
- `classification.subtype = azure-functions-isolated`;
- `classification.confidence = high`;
- sinal `sdk:Azure.Functions.Sdk`.

A identidade do SDK é autoritativa por ser um project SDK específico de Functions. O package obrigatório `Microsoft.Azure.Functions.Worker` continua fazendo parte de um projeto válido, mas não é necessário como segundo sinal do classificador.

### Isolated worker — modelo legado por package de build

Suportado deterministicamente apenas quando a forma oficial completa está presente.

Evidência necessária:

- `AzureFunctionsVersion` efetivo;
- `OutputType = Exe` efetivo;
- package avaliado `Microsoft.Azure.Functions.Worker`;
- package avaliado `Microsoft.Azure.Functions.Worker.Sdk`.

Classificação:

- `classification.kind = worker`;
- `classification.subtype = azure-functions-isolated`;
- `classification.confidence = high`.

Nenhum dos packages Worker é autoritativo isoladamente.

### Modelo in-process

Suportado deterministicamente apenas quando o par oficial runtime/build está presente.

Evidência necessária:

- `AzureFunctionsVersion` efetivo;
- `OutputType = Library` efetivo;
- package avaliado `Microsoft.NET.Sdk.Functions`.

Classificação:

- `classification.kind = library`;
- `classification.subtype = azure-functions-in-process`;
- `classification.confidence = high`.

O package isolado não é autoritativo. A propriedade de runtime e a semântica de saída de biblioteca são exigidas para evitar interpretar uma referência incidental de pacote como decisão do modelo da aplicação.

## Sinais rejeitados ou ambíguos

Os seguintes sinais não são suficientes isoladamente:

- `AzureFunctionsVersion`;
- `Microsoft.Azure.Functions.Worker`;
- `Microsoft.Azure.Functions.Worker.Sdk`;
- `Microsoft.NET.Sdk.Functions`;
- `host.json`, `local.settings.json`, nomes de projeto/pasta ou atributos de código como `Function` / `FunctionName`.

Convenções de fonte e caminho permanecem fora da fronteira de classificação.

Se os conjuntos de packages específicos dos modelos isolated e in-process aparecerem juntos, a classificação retorna `unknown` com `conflict:azure-functions-model` e sem subtipo.

Os sinais autoritativos existentes de projeto de teste mantêm precedência superior à detecção de Azure Functions.

## Fixtures

O conjunto de fixtures cobre:

- isolated worker atual com `Azure.Functions.Sdk`;
- isolated worker legado com a combinação oficial Worker/Worker.Sdk;
- in-process com `AzureFunctionsVersion` mais `Microsoft.NET.Sdk.Functions`;
- `AzureFunctionsVersion` sem evidência de modelo como non-match;
- conjunto misto intencional de packages isolated/in-process como conflito ambíguo.

A fixture do SDK atual é analisada com evaluator stub controlado para que o CI não dependa de baixar o project SDK MSBuild externo apenas para comprovar a identidade declarada do SDK. As fixtures baseadas em Microsoft.NET.Sdk usam o fluxo normal de avaliação MSBuild.

## Consequências

Azure Functions passa a ser a segunda família de subtipos suportados, depois de Blazor WebAssembly.

Não é necessário novo incremento de versão do schema porque `classification.subtype` já é opcional no schema 1.4. `AzureFunctionsVersion` é um fato interno normalizado de classificação e não é adicionado ao JSON público de inspeção.

A regra in-process deve ser reavaliada após o fim do suporte da Microsoft, mas sua detecção estrutural continua útil para inspeção de repositórios existentes e inventários de migração.
