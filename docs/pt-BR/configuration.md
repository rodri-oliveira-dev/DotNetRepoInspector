# Configuração da inspeção

**Idiomas:** [English](../en/configuration.md) | Português (Brasil)

O DotNetRepoInspector continua zero-config por padrão. Um caminho de repositório é suficiente para executar uma inspeção. A configuração opcional existe para monorepos, árvores geradas, exemplos e o pequeno conjunto de casos em que um consumidor precisa intencionalmente sobrescrever o resultado da classificação automática.

## Arquivo de configuração padrão

Quando presente na raiz do repositório inspecionado, `.dotnetrepoinspector.json` é carregado automaticamente:

```json
{
  "schemaVersion": "1",
  "exclude": [
    "generated",
    "samples/Legacy.csproj"
  ],
  "classificationOverrides": {
    "src/App/App.csproj": "web"
  }
}
```

`schemaVersion` é obrigatório e o schema atual de configuração é `1`. Propriedades desconhecidas são rejeitadas para que erros de digitação não alterem silenciosamente o comportamento da inspeção.

Todos os caminhos configurados são relativos à raiz do repositório inspecionado e devem permanecer dentro dela. Caminhos absolutos e caminhos que escapem por `..` são inválidos. Os caminhos seguem semântica relativa ao repositório; `/` é recomendado em configuração versionada.

### Exclusões

`exclude` é um array opcional. Cada entrada pode identificar:

- um diretório, fazendo com que toda a subárvore seja ignorada durante a descoberta de projetos;
- um caminho exato de projeto, fazendo com que esse projeto seja removido do conjunto descoberto.

O contrato inicial de configuração intencionalmente não implementa glob nem expressões regulares. Caminhos exatos relativos ao repositório mantêm o comportamento determinístico e portável entre runners.

As exclusões internas da descoberta, como diretórios normais de saída de build, continuam valendo independentemente desse arquivo.

### Overrides de classificação

`classificationOverrides` é um objeto opcional cujas chaves são caminhos de projetos relativos ao repositório e cujos valores são um dos seguintes:

- `web`
- `worker`
- `console`
- `library`
- `test`
- `unknown`

Um override altera apenas a **interpretação efetiva da classificação do projeto**. Ele não altera SDKs, target frameworks, `OutputType`, metadados de teste, packability, runtime identifiers, referências nem qualquer outro fato coletado pelo MSBuild.

Quando um override é aplicado, o schema `1.3` o torna distinguível da classificação automática:

```json
"classification": {
  "kind": "web",
  "signals": [
    "output-type:library"
  ],
  "source": "configuration",
  "automaticKind": "library"
}
```

Os sinais automáticos continuam presentes. `automaticKind` registra o resultado original do classificador, `kind` contém o override efetivo, `source` identifica a origem do override e a `confidence` automática não é reutilizada como confiança de uma decisão manual.

Se um override apontar para um projeto que não foi descoberto, a inspeção continua e emite `DRI1014` com severidade `warning`. Isso torna configuração obsoleta visível sem transformá-la em falha de inspeção.

## Configuração pela CLI

A CLI expõe os mesmos conceitos diretamente:

```bash
dotnet repo-inspect . \
  --exclude generated \
  --exclude samples/Legacy.csproj \
  --classify src/App/App.csproj=web
```

Use um arquivo diferente do padrão com:

```bash
dotnet repo-inspect . --config config/inspector.json
```

Desabilite o carregamento automático de `.dotnetrepoinspector.json` com:

```bash
dotnet repo-inspect . --no-config
```

`--config` e `--no-config` não podem ser usados juntos. `--exclude` e `--classify` são repetíveis.

## Configuração pela GitHub Action

A Action reutilizável expõe os mesmos conceitos. `exclude` e `classify` recebem valores separados por linhas:

```yaml
- name: Inspecionar repositório .NET
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
    exclude: |
      generated
      samples/Legacy.csproj
    classify: |
      src/App/App.csproj=web
```

Um arquivo de configuração customizado pode ser informado em `config`; `no-config: "true"` desabilita o carregamento automático do arquivo padrão.

A Action encaminha esses valores para o mesmo contrato de configuração da CLI/Engine. Ela não implementa um segundo parser de configuração nem outra camada de classificação.

## Precedência

A configuração é resolvida de forma determinística:

1. o comportamento interno do Inspector fornece o baseline zero-config;
2. `.dotnetrepoinspector.json`, ou o arquivo explícito selecionado por `--config` / input `config` da Action, contribui com exclusões e overrides de classificação;
3. valores diretos da requisição vindos da CLI/Action são aplicados por último.

Exclusões são aditivas: valores de `--exclude` / input `exclude` da Action são combinados com as exclusões do arquivo.

Na classificação, uma entrada direta de `--classify` / Action `classify` para o mesmo projeto substitui a entrada do arquivo. No JSON resultante, `classification.source` será `request`; um override apenas do arquivo usa `configuration`.

`--no-config` / Action `no-config` remove completamente a camada do arquivo. Exclusões e overrides diretos continuam sendo aplicados.

## Configuração inválida

Configuração inválida do repositório é representada no contrato normal da inspeção como `DRI1013` com severidade `error`. Exemplos incluem:

- JSON inválido;
- `schemaVersion` de configuração não suportado;
- propriedades desconhecidas no arquivo;
- arquivo explícito de configuração inexistente;
- caminho configurado absoluto ou que escape da raiz;
- tipo de classificação não suportado;
- semântica conflitante de `--config` e `--no-config` na fronteira da Engine.

A Engine retorna um `InspectionReport` contendo o diagnóstico em vez de descartar o resultado legível por máquina. A CLI, portanto, termina com código `1`, e a GitHub Action preserva o mesmo código enquanto expõe o caminho do relatório quando disponível.

Erros de sintaxe da linha de comando detectados antes da Engine, como um valor malformado de `--classify`, continuam sendo argumentos inválidos da CLI e terminam com código `2`.
