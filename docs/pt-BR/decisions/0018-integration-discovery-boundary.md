# ADR 0018: Manter Integration Discovery opt-in e isolado

- Status: Aceita
- Data: 2026-10-01

## Contexto

O contrato estável de inspeção é baseado em fatos avaliados do MSBuild e do Git. Descobrir integrações outbound exige inspeção limitada de source C#, mas source code é uma entrada sensível e não pode se tornar conteúdo coletado. Regras de providers também precisam evoluir sem acoplar Core ou Engine a SDKs individuais.

## Decisão

`DotNetRepoInspector.Core` mantém somente o contrato normalizado `IntegrationFinding` e sua validação. `DotNetRepoInspector.IntegrationDiscovery` mantém o tratamento de source e o ponto de extensão `IIntegrationDetector`. Detectores concretos permanecem isolados atrás dessa interface. A Engine invocará um único pipeline de discovery somente quando um único opt-in do produto estiver habilitado; adapters de delivery exporão essa opção sem implementar regras de detecção.

Discovery é somente sintático. Ele não compila, carrega, invoca nem executa de qualquer outra forma o código do repositório alvo. MSBuild e Git permanecem autoritativos para fatos estruturados do repositório, enquanto a sintaxe de source fornece somente evidência de integração com proveniência de caminho relativo e linha.

Cada execução deve impor limites configuráveis para caminhos visitados, arquivos elegíveis, bytes lidos, findings retornados e tempo de trabalho. Cancellation deve ser propagado pela enumeração, parsing e detectores. Atingir um limite retorna output parcial determinístico com metadata ou diagnóstico explícito de truncation; nunca amplia o scan silenciosamente.

Somente evidência em allow-list pode entrar em `InspectionReport`: nomes lógicos de recursos, hostnames quando observados diretamente, chaves de configuração sem valores, nomes de tipos ou contracts, localizações, confidence e códigos de signal estáveis. Bodies de source, payloads, queries, connection strings, valores de configuração, credenciais, tokens e dados de autenticação nunca são findings ou diagnostics.

O schema `1.6` adiciona `integrations` opcional no nível superior. A serialização canônica emite um array vazio quando discovery está desabilitado ou não produz findings, enquanto payloads `1.x` compatíveis e mais antigos podem omiti-lo. IDs derivam da identidade canônica do finding, caminhos e signals são normalizados e a ordenação é determinística.

## Consequências

- O comportamento e custo existentes da inspeção permanecem inalterados salvo quando discovery é habilitado.
- Core permanece independente de Roslyn e SDKs de providers.
- Autores de detectores recebem source como entrada transitória de análise, mas não podem adicionar campos arbitrários ao output.
- Análise somente sintática é intencionalmente conservadora e não prova topologia runtime ou ownership remoto.
- Defaults numéricos e comportamento operacional de truncation pertencem à implementação do pipeline de discovery e podem evoluir independentemente da forma pública do finding.

## Alternativas rejeitadas

- Tornar análise de source obrigatória mudaria o custo padrão e os trust boundaries.
- Colocar lógica de provider na Engine ou nos adapters de delivery duplicaria comportamento e inverteria a direção das dependências.
- Serializar sintaxe arbitrária, valores de configuração ou property bags completos criaria uma superfície inaceitável de exposição de dados.
- Exigir compilação semântica executaria um toolchain mais amplo e menos previsível e não é necessário para o primeiro catálogo.
