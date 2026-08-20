# ADR 0001: Avaliar projetos por meio de `dotnet msbuild`

**Idiomas:** [English](../../en/decisions/0001-msbuild-evaluation-strategy.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-08-19
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O DotNetRepoInspector precisa inspecionar os metadados efetivos de projetos .NET. Ler diretamente o XML do `.csproj` é insuficiente porque os valores efetivos podem vir de imports de SDK, `Directory.Build.props`, `Directory.Build.targets`, grupos condicionais de propriedades e outros imports do MSBuild.

A primeira implementação também precisa funcionar em CI sem acoplar `DotNetRepoInspector.Core` a um runtime específico de MSBuild ou ao ambiente do GitHub Actions.

## Decisão

O adapter de MSBuild avaliará os projetos fora do processo por meio da CLI do .NET.

Para cada avaliação de projeto:

1. Resolver o caminho absoluto do projeto e usar o diretório do projeto como working directory do processo.
2. Executar `dotnet --version` como uma etapa de preflight para resolução do SDK.
3. Se a resolução do SDK for bem-sucedida, executar:

   ```text
   dotnet msbuild <project> -nologo -verbosity:quiet -getProperty:<property1,property2,...>
   ```

4. Não solicitar targets de build quando apenas metadados disponíveis no momento da avaliação forem necessários.
5. Fazer parsing do JSON estruturado retornado pelo MSBuild quando múltiplas propriedades forem solicitadas; suportar o formato textual retornado para uma única propriedade.
6. Retornar resultados normalizados e códigos de erro pertencentes ao adapter, em vez de expor tipos `Microsoft.Build.*` ou objetos brutos de processo.

O contrato inicial do adapter é `IMsBuildProjectEvaluator`. `DotNetRepoInspector.Core` permanece alheio à execução de processos e aos detalhes de implementação do MSBuild.

## Seleção do SDK

`dotnet --version` e `dotnet msbuild` são iniciados a partir do diretório do projeto inspecionado. Assim, a CLI do .NET aplica seu comportamento normal de busca por `global.json` e roll-forward a partir desse local e de seus diretórios ancestrais.

O resultado do preflight também é usado para distinguir falhas de resolução do SDK de falhas de avaliação de projeto pelo MSBuild sem fazer parsing de mensagens de erro localizadas.

As categorias iniciais de erro são:

- `InvalidRequest`;
- `ProjectNotFound`;
- `DotNetHostNotFound`;
- `SdkResolutionFailed`;
- `MsBuildEvaluationFailed`;
- `InvalidMsBuildOutput`.

A issue #4 expandirá os metadados de SDK no nível do repositório (`global.json`, SDK configurado e SDK resolvido) sem alterar essa fronteira de avaliação.

## Por que não usar o XML bruto do projeto?

O XML bruto não consegue produzir de forma confiável os valores efetivos após imports, defaults de SDK e condições. O parsing de XML ainda poderá ser usado no futuro para cenários restritos de descoberta/bootstrap, mas não é a fonte da verdade para metadados avaliados do projeto.

## Por que não usar `Microsoft.Build.*` dentro do processo?

Usar `Microsoft.Build`, `Microsoft.Build.Locator` ou APIs equivalentes dentro do processo forneceria um modelo de objetos rico, mas introduziria preocupações adicionais para a primeira versão:

- selecionar e carregar uma instância de MSBuild/SDK no processo do Inspector;
- carregamento de assemblies no processo inteiro e comportamento do SDK resolver;
- compatibilidade ao inspecionar repositórios que selecionam diferentes feature bands de SDK;
- acoplamento mais forte entre o runtime do Inspector e as ferramentas de build do repositório inspecionado;
- isolamento e recuperação de falhas mais complexos.

A fronteira da CLI fora do processo permite que as regras normais de seleção do SDK .NET do repositório inspecionado controlem a avaliação e mantém essas dependências fora do Core.

## Por que não executar um target de build/design-time?

Os metadados iniciais necessários pelo Inspector são dados disponíveis no momento da avaliação. O MSBuild permite consultar propriedades e itens após a avaliação sem especificar um target. Executar targets de build ou design-time adicionaria trabalho e efeitos colaterais desnecessários neste estágio.

Se um fato futuro só puder ser produzido pela execução de um target, esse comportamento deverá ser introduzido explicitamente e documentado separadamente.

## Consequências

### Positivas

- Imports e condições efetivas do MSBuild são respeitados.
- O mesmo comportamento do adapter pode ser usado localmente e em CI.
- O Core permanece independente de `Microsoft.Build.*` e `System.Diagnostics.Process`.
- Falhas de resolução do SDK e de avaliação de projeto possuem códigos de erro estruturados distintos.
- O cancelamento do processo pode encerrar toda a árvore de processos filhos.
- A passagem de argumentos utiliza `ProcessStartInfo.ArgumentList`; nenhum comando de shell é construído.

### Trade-offs

- Cada avaliação exige um ou mais processos filhos, o que possui custo de inicialização.
- Repositórios grandes podem exigir batching/caching ou outra estratégia de otimização no futuro.
- O parsing da saída da CLI passa a ser responsabilidade do adapter.
- O SDK alvo precisa estar disponível no ambiente de execução.

Performance e escalabilidade são deliberadamente adiadas para a issue #18, para que a otimização seja orientada por medições.

## Nota de segurança

Não solicitar targets de build reduz execução desnecessária, mas a avaliação do MSBuild não é um sandbox nem uma fronteira de segurança. Imports, resolução de SDK, propriedades originadas do ambiente e property functions ainda são processados durante a evaluation e podem acessar recursos disponíveis ao processo filho.

O DotNetRepoInspector aplica controles de defesa em profundidade nessa fronteira: variáveis de ambiente com nomes que indicam credenciais e handles de arquivos de comando do CI são removidos dos processos `dotnet`/MSBuild criados pelo Inspector, a reutilização de nodes do MSBuild é desabilitada, argumentos não passam por shell e saída bruta de processos não é mapeada para o relatório normalizado. Esses controles reduzem exposição, mas não tornam segura a avaliação de repositórios não confiáveis em ambientes privilegiados ou contendo secrets.

O modelo de confiança completo e as orientações operacionais são mantidos em [`../security.md`](../security.md).

## Referências

- Microsoft Learn — Evaluate MSBuild items and properties: https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties
- Microsoft Learn — MSBuild command-line reference: https://learn.microsoft.com/visualstudio/msbuild/msbuild-command-line-reference
- Microsoft Learn — Secure MSBuild usage best practices: https://learn.microsoft.com/visualstudio/msbuild/msbuild-security-best-practices
- Microsoft Learn — visão geral do `global.json`: https://learn.microsoft.com/dotnet/core/tools/global-json
