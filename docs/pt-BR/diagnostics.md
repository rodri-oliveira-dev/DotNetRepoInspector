# Diagnósticos e logging operacional

**Idiomas:** [English](../en/diagnostics.md) | Português (Brasil)

O DotNetRepoInspector separa **diagnósticos de inspeção** de **logs operacionais**.

Os diagnósticos de inspeção fazem parte do resultado normalizado e são destinados tanto a pessoas quanto à automação. Os logs operacionais explicam como a ferramenta foi executada e nunca alteram o schema normalizado da inspeção.

## Catálogo estável de diagnósticos

| Código | Severidade padrão | Significado |
| --- | --- | --- |
| `DRI1001` | `error` | Um projeto não pôde ser inspecionado ou seu arquivo de projeto não pôde ser lido. |
| `DRI1002` | `error` | O SDK .NET necessário não pôde ser resolvido. |
| `DRI1003` | `warning` | Uma referência de projeto não pôde ser resolvida. |
| `DRI1004` | `warning` | Uma propriedade esperada do projeto não pôde ser avaliada. |
| `DRI1005` | `error` | O `global.json` aplicável é inválido. |
| `DRI1006` | `error` | O MSBuild não conseguiu avaliar um projeto. |
| `DRI1007` | `error` | O MSBuild retornou um resultado estruturado inválido. |
| `DRI1008` | `error` | O host .NET não pôde ser iniciado. |
| `DRI1009` | `error` | A requisição de inspeção é inválida. |
| `DRI1010` | `error` | A raiz do repositório está indisponível. |
| `DRI1011` | `error` | O `global.json` aplicável não pôde ser lido. |
| `DRI1012` | `warning` | Os metadados do repositório não puderam ser coletados completamente. |
| `DRI1013` | `error` | A configuração da inspeção é inválida, não suportada, ilegível ou viola regras de caminho/classificação. |
| `DRI1014` | `warning` | Um override de classificação configurado não correspondeu a um projeto descoberto. |

Os códigos são identificadores estáveis. Códigos existentes não devem ser reutilizados com outro significado. A automação deve utilizar `code` e `severity`, e não o texto de `message`.

Para `DRI1013`, `context.reason` fornece um motivo estável e não sensível, como `invalid-json`, `unsupported-config-schema`, `config-file-not-found`, `invalid-excluded-path` ou `invalid-classification-kind`. Detalhes da configuração que possam conter conteúdo arbitrário do repositório não são copiados para os diagnósticos.

Para `DRI1014`, `source` identifica o caminho do projeto configurado, relativo ao repositório, e `context.overrideSource` informa se o override obsoleto veio de `configuration` ou da camada direta `request`.

## Campos de diagnóstico

- `code`: identificador estável no formato `DRIxxxx`.
- `severity`: `info`, `warning` ou `error`.
- `message`: resumo estável e legível por pessoas, definido pelo DotNetRepoInspector.
- `source`: caminho normalizado opcional ou identificador do componente associado ao diagnóstico.
- `details`: detalhe textual opcional e controlado. Não deve ser a única fonte de semântica para máquinas.
- `context`: mapa estruturado opcional de strings. Chaves e valores não devem conter dados sensíveis e são serializados em ordem determinística de chaves.

Adapters de infraestrutura e a fronteira de configuração traduzem falhas internas para este catálogo. Mensagens de erro brutas e localizadas não são necessárias para classificar a falha.

## Escopo dos diagnósticos e agregação de saúde

O escopo dos diagnósticos é estrutural e deve ser preservado pelos consumidores:

- `diagnostics` no nível superior pertence ao escopo do repositório/inspeção;
- `projects[].diagnostics` pertence somente àquele projeto;
- um diagnóstico em um projeto não altera a saúde dos projetos irmãos;
- o exit code da CLI é um resultado agregado da execução. O código `1` significa que existe pelo menos um diagnóstico de erro no escopo do repositório ou em algum projeto. Ele não deve ser copiado para todos os projetos como status individual.

A API do Core expõe `InspectionHealthEvaluator` para agregação determinística. `RepositoryStatus` é derivado somente dos diagnósticos de nível superior, `OverallStatus` considera os dois escopos e os contadores de projetos usam apenas a coleção de diagnósticos de cada projeto. `GetProjectStatus(project)` retorna `ok`, `warning` ou `error` sem consultar diagnósticos do repositório.

Consumidores do JSON podem calcular as mesmas métricas sem criar uma regra paralela de status. Por exemplo:

```jq
def status($diagnostics):
  if any($diagnostics[]; .severity == "error") then "error"
  elif any($diagnostics[]; .severity == "warning") then "warning"
  else "ok"
  end;

{
  repositoryStatus: status(.diagnostics),
  projectsWithDiagnostics:
    ([.projects[] | select((.diagnostics | length) > 0)] | length),
  projectsWithWarnings:
    ([.projects[] | select(any(.diagnostics[]; .severity == "warning"))] | length),
  projectsWithErrors:
    ([.projects[] | select(any(.diagnostics[]; .severity == "error"))] | length)
}
```

Esses contadores separam intencionalmente a quantidade de projetos afetados da quantidade de diagnósticos. Um projeto com vários diagnósticos continua sendo apenas um projeto afetado.

## Logs operacionais

Logs operacionais são emitidos em **stderr**. A saída JSON pertence exclusivamente a **stdout**. Essa separação permite que consumidores façam pipe ou parsing de stdout como JSON mesmo quando o logging detalhado está habilitado.

A CLI oferece estes modos de verbosidade:

- normal: apenas eventos operacionais informativos, warnings e erros;
- `--verbose` ou `-v`: também emite contexto detalhado de execução;
- `--debug`: emite contexto detalhado e de debug.

O logging de debug não deve imprimir argumentos brutos da linha de comando, variáveis de ambiente, conteúdo do código-fonte, dumps de processo, credenciais, headers de autorização, connection strings, tokens ou secrets.

`CliLogger` aceita contexto estruturado em strings e aplica redaction em profundidade a chaves que pareçam sensíveis. Ainda assim, os chamadores devem usar mensagens estáveis e não sensíveis e evitar incorporar secrets diretamente nas mensagens de log.
