# Política de compatibilidade

**Idiomas:** [English](../en/compatibility.md) | Português (Brasil)

Este documento define o que o DotNetRepoInspector atualmente garante entre versões do .NET SDK e sistemas operacionais.

## Runtime do Inspector

O próprio DotNetRepoInspector é compilado para **`net10.0`**. O `global.json` do repositório seleciona um SDK .NET 10 estável (`10.0.100` com roll-forward `latestFeature` e prerelease desabilitado).

O runtime usado para executar o Inspector é independente do SDK selecionado pelo repositório inspecionado. Um Inspector `net10.0` pode inspecionar um repositório cujos projetos usam `net8.0`, desde que o SDK necessário para avaliar esse repositório esteja instalado.

## Matriz obrigatória de suporte

As combinações abaixo fazem parte do gate obrigatório de compatibilidade no CI:

| Sistema operacional | Runtime do Inspector | Família do SDK alvo | Target framework | Garantia |
| --- | --- | --- | --- | --- |
| Linux | `net10.0` | .NET 8 | `net8.0` | Obrigatória |
| Linux | `net10.0` | .NET 10 | `net10.0` | Obrigatória |
| Windows | `net10.0` | .NET 8 | `net8.0` | Obrigatória |
| Windows | `net10.0` | .NET 10 | `net10.0` | Obrigatória |
| macOS | `net10.0` | .NET 8 | `net8.0` | Obrigatória |
| macOS | `net10.0` | .NET 10 | `net10.0` | Obrigatória |

O CI instala os SDKs .NET 8 e .NET 10 lado a lado e comprova que o `global.json` do repositório inspecionado controla a resolução do SDK independentemente do runtime do Inspector.

`net9.0` e outras combinações de SDK/TFM não fazem parte da matriz obrigatória. Elas podem ser inspecionáveis quando um SDK compatível estiver instalado, mas não constituem uma garantia de compatibilidade protegida por release até serem adicionadas a esta matriz.

## O que significa "suportado"

Suporte significa que o DotNetRepoInspector consegue:

1. descobrir arquivos de projeto suportados;
2. resolver o SDK do repositório conforme o `global.json` e as regras normais de resolução do `dotnet`;
3. avaliar o projeto com `dotnet msbuild`;
4. extrair os fatos normalizados usados pelo contrato de inspeção;
5. produzir JSON determinístico e diagnósticos estruturados.

Suporte **não** significa que o Inspector restaura, compila, testa, publica ou executa a aplicação inspecionada. Portanto, um projeto pode ser inspecionável mesmo quando workloads ou dependências de runtime exigidas para um build completo não estiverem disponíveis.

## Disponibilidade do SDK

O SDK exigido pelo repositório inspecionado deve estar instalado no host quando o `global.json` não puder fazer roll-forward para outro SDK compatível instalado.

Se a resolução do SDK falhar, o Inspector mantém a falha consumível por automação e emite o diagnóstico **`DRI1002` (`DotNetSdkUnavailable`)** com severidade `error`. A CLI ainda pode produzir um relatório para essa inspeção parcial e retorna o código não zero documentado para resultado parcial.

## Paths, casing e line endings

Paths de projetos no contrato legível por máquina são relativos ao repositório e normalizados com separadores `/`, inclusive no Windows.

O casing do path é preservado. O DotNetRepoInspector não altera a capitalização de paths públicos; a resolução no filesystem continua seguindo o comportamento do sistema operacional. Os testes, portanto, usam o casing relativo exato em vez de assumir que todos os hosts são case-insensitive.

Arquivos de projeto/configuração com LF ou CRLF são entradas válidas. O próprio repositório normaliza seus arquivos versionados, mas repositórios inspecionados não precisam seguir a mesma política de line ending.

## SDKs preview

SDKs preview do .NET **não** fazem parte da matriz obrigatória de compatibilidade.

Um SDK preview pode ser usado em modo best-effort quando todas as condições abaixo forem atendidas:

- o SDK preview estiver explicitamente instalado no host;
- o repositório inspecionado o selecionar pelas regras normais do `global.json`;
- a resolução de prerelease estiver explicitamente permitida quando necessário.

Comportamento em preview não é gate de release e pode mudar conforme previews upstream do SDK/MSBuild. Um preview só se torna garantido quando for adicionado intencionalmente à matriz do CI.

## Validação no CI

O workflow `Validate .NET` contém uma matriz cross-platform para Ubuntu, Windows e macOS. Cada entrada da matriz:

- instala .NET 8 e o SDK .NET 10 do Inspector lado a lado;
- compila o Inspector com .NET 10;
- inspeciona repositórios sintéticos `net8.0` e `net10.0`;
- valida o SDK selecionado para cada repositório inspecionado;
- valida separadores de path normalizados e casing preservado;
- valida o diagnóstico consistente `DRI1002` para SDK indisponível;
- avalia um projeto temporário escrito com line endings CRLF.
