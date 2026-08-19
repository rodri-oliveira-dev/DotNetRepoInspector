# Engine de inspeção

**Idiomas:** [English](../en/inspection-engine.md) | Português (Brasil)

`DotNetRepoInspector.Engine` é a camada de aplicação reutilizável que executa uma inspeção completa do repositório em uma única operação. Ela é intencionalmente independente da CLI, GitHub Actions, persistência e integrações de políticas/relatórios.

## API

O engine expõe `IRepositoryInspector`:

```csharp
Task<InspectionReport> InspectAsync(
    RepositoryInspectionRequest request,
    CancellationToken cancellationToken = default);
```

`RepositoryInspectionRequest` contém a raiz do repositório e, opcionalmente, diretórios relativos ao repositório que devem ser excluídos da descoberta.

O `RepositoryInspector` padrão compõe os adapters atuais nesta ordem:

1. metadados Git do repositório;
2. configuração do SDK .NET e SDK resolvido;
3. descoberta de projetos;
4. fatos MSBuild avaliados para cada projeto descoberto;
5. classificação determinística dos projetos;
6. grafo avaliado de `ProjectReference`;
7. diagnósticos normalizados no nível do repositório e dos projetos;
8. construção estável do `InspectionReport`.

Atualmente, a avaliação de projetos é sequencial. Isso mantém a pressão sobre processos limitada e o comportamento da saída simples enquanto o engine estabelece sua baseline; a avaliação paralela poderá ser considerada posteriormente com base em medições, em vez de ser introduzida implicitamente.

## Semântica de falhas

O engine diferencia falhas que tornam a requisição de inspeção impossível de falhas que afetam apenas parte das evidências.

### Falhas fatais

Falhas fatais interrompem a operação em vez de produzir um relatório parcial potencialmente enganoso:

- o argumento da raiz do repositório está vazio ou é inválido;
- a raiz solicitada do repositório não existe;
- a descoberta de projetos não consegue estabelecer o inventário de projetos;
- o cancelamento é solicitado.

O cancelamento nunca é convertido em um diagnóstico de inspeção. `OperationCanceledException` é propagada para o chamador.

### Falhas parciais

Falhas parciais preservam todas as informações que ainda podem ser inspecionadas:

- os metadados Git estão indisponíveis ou incompletos: os campos do repositório permanecem ausentes e um warning no nível do repositório pode ser emitido;
- a inspeção do SDK falha: a falha se torna um diagnóstico no nível do repositório, enquanto a avaliação dos projetos ainda é tentada quando possível;
- a avaliação MSBuild de um projeto falha: esse projeto permanece em `projects` com seu caminho/nome e um diagnóstico de erro no nível do projeto; os demais projetos continuam normalmente;
- um destino de `ProjectReference` está ausente: a aresta é preservada e `DRI1003` é associado ao projeto de origem.

Essa distinção permite que a automação examine os diagnósticos normalizados e aplique sua própria política sem perder fatos válidos dos projetos.

## Determinismo

Para o mesmo estado do repositório e toolchain, o relatório normalizado é determinístico:

- projetos descobertos são processados e emitidos em ordem ordinal de caminho;
- SDKs dos projetos, target frameworks, runtime identifiers e referências são normalizados e ordenados;
- diagnósticos são ordenados antes da construção do relatório;
- o serializer público realiza sua própria canonicalização como fronteira final do contrato.

Caminhos absolutos específicos da máquina permanecem apenas dentro dos adapters e da orquestração. Caminhos de projetos e referências no relatório público permanecem relativos ao repositório e usam `/` como separador.

## Cancelamento

O `CancellationToken` fornecido é propagado para processos Git, inspeção do SDK, descoberta de projetos e todas as avaliações MSBuild de projetos. A descoberta no filesystem verifica o cancelamento durante a travessia de diretórios e arquivos, permitindo interromper um repositório grande sem aguardar a conclusão de todo o scan.
