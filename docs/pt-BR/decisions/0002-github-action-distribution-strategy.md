# ADR 0002: Distribuir a GitHub Action como Composite Action sobre a .NET Tool

**Idiomas:** [English](../../en/decisions/0002-github-action-distribution-strategy.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-08-19
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O DotNetRepoInspector já possui uma fronteira de CLI e pode ser empacotado como a .NET Tool `DotNetRepoInspector`. A próxima fronteira de entrega é uma GitHub Action reutilizável com a experiência esperada para o consumidor:

```yaml
- name: Inspecionar repositório .NET
  id: inspect
  uses: rodri-oliveira-dev/DotNetRepoInspector@v1
  with:
    path: .
```

A Action não deve se tornar uma segunda implementação da descoberta de repositórios, avaliação MSBuild, classificação, diagnósticos ou serialização JSON. Esses comportamentos pertencem aos contratos existentes da engine e da CLI.

A estratégia de distribuição precisa equilibrar custo de inicialização, compatibilidade de runners, disponibilidade de runtime, semântica de release/versionamento e exposição da supply chain. Ela também precisa funcionar de forma natural para repositórios .NET sem exigir um ambiente de execução exclusivo de Docker nem uma implementação separada em JavaScript do comportamento de inspeção.

## Decisão

A GitHub Action pública será uma **Composite Action** cujo único executor da inspeção será uma **versão exata e fixada por release da .NET Tool `DotNetRepoInspector`**.

O `action.yml` público ficará na raiz do repositório para que os consumidores possam usar diretamente `rodri-oliveira-dev/DotNetRepoInspector@<ref>`.

A Composite Action será responsável apenas por aspectos de entrega:

1. validar e normalizar os inputs da Action necessários para invocar a CLI;
2. garantir que um runtime/SDK .NET compatível esteja disponível para o próprio Inspector;
3. instalar uma versão exata do pacote `DotNetRepoInspector` em um tool path temporário pertencente à Action;
4. invocar `dotnet-repo-inspect` com o caminho solicitado do repositório e o arquivo de saída;
5. expor outputs pequenos e orientados à automação derivados do resultado da CLI;
6. preservar a semântica de códigos de saída da CLI.

Ela não deve reimplementar descoberta de projetos, interpretação de MSBuild, classificação, diagnósticos, extração de metadados do repositório ou o contrato JSON.

## Bootstrap de runtime

A Action não deve depender de uma versão específica do SDK .NET estar presente por acaso na imagem de um runner hospedado pelo GitHub.

Para o runtime do Inspector, a Action garantirá que o SDK/runtime .NET necessário esteja disponível usando um mecanismo de setup confiável e fixado. Se `actions/setup-dotnet` for utilizado pela Composite Action, a dependência deverá ser fixada por SHA completo de commit nas revisões publicadas da Action, em vez de uma branch ou tag major flutuante.

O bootstrap poderá evitar download quando um SDK/runtime compatível com o Inspector já estiver disponível, mas isso é uma otimização, não um contrato.

Esse bootstrap garante apenas o runtime necessário para executar o DotNetRepoInspector. **Os SDKs exigidos pelo repositório inspecionado continuam sendo responsabilidade de quem chama a Action.** Isso é necessário porque os repositórios inspecionados podem selecionar diferentes SDKs por meio de `global.json`, incluindo SDKs diferentes do próprio runtime alvo do Inspector. A Action não deve adivinhar nem instalar silenciosamente matrizes arbitrárias de SDKs do repositório.

## Instalação da Tool

A Action instalará a ferramenta em um diretório pertencente à invocação atual da Action sob o diretório temporário do runner, usando `dotnet tool install --tool-path` ou mecanismo isolado equivalente.

Ela não instalará a ferramenta globalmente e não modificará o tool manifest local do repositório inspecionado. Isso evita alterações persistentes de estado do usuário, colisões de comandos e mutações no repositório.

Cada ref publicado da Action deverá resolver deterministicamente uma versão **exata** do pacote `DotNetRepoInspector`. Tags completas resolvem diretamente; aliases móveis e SHAs de commit resolvem pela única tag SemVer completa e imutável no mesmo commit da release. Wildcards, `latest`, resolução implícita da versão estável e seleção implícita de prerelease não são permitidos.

A implementação inicial não exporá um input `inspector-version`. Permitir que quem chama substitua a versão exata resolvida da Tool faria com que uma mesma ref da Action pudesse produzir schemas e comportamentos diferentes ao longo do tempo, enfraquecendo a reprodutibilidade. Se um override for introduzido no futuro, isso exigirá um design explícito de compatibilidade e supply chain.

## Isolamento da origem do pacote

O bootstrap da Tool não deve confiar nos arquivos `NuGet.config` do repositório inspecionado para resolver o pacote do Inspector.

A Action deverá usar uma configuração NuGet temporária e isolada para a própria instalação da Tool, removendo package sources herdados e selecionando explicitamente a origem pública pretendida do pacote. Isso impede que um feed controlado pelo repositório ou configurado na máquina faça shadowing do ID/versão do pacote `DotNetRepoInspector` durante o bootstrap da Action.

Esse isolamento se aplica apenas à instalação da Tool do Inspector. Ele não deve reescrever a configuração de package sources utilizada pelo próprio repositório inspecionado.

## Inputs da Action

O contrato esperado para a Action v1 deverá expor os seguintes inputs de alto nível:

| Input | Obrigatório | Padrão | Significado |
| --- | --- | --- | --- |
| `path` | Não | `.` | Caminho do repositório a inspecionar, relativo ao workspace quando não for absoluto. |
| `output` | Não | Arquivo temporário pertencente à Action | Destino do JSON de inspeção. |
| `verbosity` | Não | `normal` | Nível de logging operacional: `normal`, `verbose` ou `debug`. |

A Action não deve realizar checkout implicitamente. Os consumidores controlam explicitamente `actions/checkout` e sua semântica de permissões/ref antes de invocar o Inspector.

Inputs adicionais de política não devem ser adicionados apenas para encapsular comportamento da CLI. Em particular, a v1 preservará a semântica existente dos códigos de saída da CLI em vez de inventar uma segunda interpretação específica da Action para erros de inspeção.

## Outputs da Action

O contrato esperado para a Action v1 deverá expor outputs pequenos e adequados à automação posterior:

| Output | Significado |
| --- | --- |
| `report-path` | Caminho absoluto para o JSON de inspeção gerado quando existir um relatório. |
| `schema-version` | Versão do schema lida do relatório gerado quando disponível. |
| `inspector-version` | Versão exata da .NET Tool resolvida para este ref de release da Action. |
| `exit-code` | Código de saída retornado pela CLI. |

O JSON completo da inspeção **não** será duplicado em um output do GitHub Actions. Relatórios podem ser materialmente maiores que outputs normais de steps, e um arquivo já é a fronteira canônica legível por máquina suportada pela CLI. Steps posteriores devem ler `report-path`.

Ler `schemaVersion` do JSON gerado para preencher um output é apenas plumbing da camada de entrega; isso não deve se transformar em uma implementação independente da serialização JSON ou do schema.

## Comportamento de saída

A Action preservará os códigos de saída estáveis da CLI em vez de reduzi-los a um modelo genérico de sucesso/falha.

O wrapper deverá capturar o código de saída da CLI, publicar os outputs que ainda puderem ser determinados e então finalizar com o mesmo status não zero quando a CLI falhar. Assim, um relatório produzido com diagnósticos de erro continua distinguível de uma falha fatal de acordo com o contrato da CLI.

Isso mantém o comportamento da execução local da CLI alinhado ao da execução pelo GitHub Actions.

## Estratégia de versionamento

A GitHub Action e a .NET Tool são publicadas a partir do mesmo repositório e usarão uma única versão de release do produto.

Para uma release completa `v1.2.3`:

- a tag imutável da release completa da Action é `v1.2.3`;
- a tag imutável, os aliases móveis e o SHA do commit da release resolvem exatamente para a versão `1.2.3` do pacote `DotNetRepoInspector`;
- os aliases móveis de compatibilidade `v1` e, quando mantido, `v1.2` apontam para a release completa compatível mais recente;
- a automação de release não deve mover um alias de compatibilidade antes que o pacote exato correspondente tenha sido publicado e validado.

Consumidores que priorizam conveniência de atualização podem usar `@v1`. Consumidores que priorizam reprodutibilidade podem usar uma tag imutável de release completa ou um SHA completo de commit.

### Relação com o schema JSON

A versão major da Action é uma fronteira de compatibilidade tanto para a interface da Action quanto para o contrato público de inspeção consumido por meio dela.

- A Action `v1` só pode fixar releases do Inspector cuja saída permaneça compatível com o schema major `1`.
- Alterações aditivas/retrocompatíveis do Inspector e do schema podem ser lançadas dentro da Action `v1` de acordo com versionamento semântico.
- Uma alteração breaking nos inputs/outputs da Action exige uma nova versão major da Action.
- Uma alteração breaking no schema de inspeção também exige uma nova versão major da Action, mesmo que os inputs do `action.yml` não tenham mudado.

Isso impede que uma tag móvel `v1` atravesse silenciosamente uma fronteira de contrato legível por máquina.

## Cache e custo de inicialização

A primeira versão da Action **não** adicionará sua própria camada de `actions/cache` para a Tool instalada.

A versão da Tool é exata, e o cliente .NET/NuGet poderá reutilizar caches já presentes no ambiente. Adicionar um cache gerenciado pela Action introduz preocupações de chave de cache, poisoning, invalidação e diferenças entre plataformas que não se justificam sem medições.

Se o tempo de inicialização da Action se tornar relevante, o bootstrap de runtime e a instalação da Tool deverão ser medidos separadamente antes de introduzir cache ou mudar a estratégia de distribuição. Um artefato self-contained de release continua sendo uma otimização futura válida se as medições mostrarem que o bootstrap do .NET/Tool domina o tempo de execução.

## Permissões e segurança

A própria Action de inspeção não exige chamada à API do GitHub nem token do GitHub para um checkout local. Ela não deve solicitar permissões de escrita nem exigir segredo para uma inspeção normal.

As revisões publicadas da Action deverão seguir estas regras de supply chain:

- fixar actions aninhadas, sejam first-party ou third-party, por SHA completo de commit;
- resolver o pacote do Inspector para uma versão exata e imutável da release;
- isolar a origem do pacote do Inspector de configurações NuGet controladas pelo repositório;
- não executar versões de pacote selecionadas por ranges flutuantes;
- publicar uma release da Action somente depois que o pacote correspondente tiver passado build, testes, empacotamento e validação de instalação;
- manter credenciais de publicação fora de workflows de validação de pull requests.

A inspeção do repositório em si não é um sandbox. A ADR 0001 estabelece que a avaliação de projetos usa `dotnet msbuild`; portanto, lógica de MSBuild importada e configuração de build controlada pelo repositório precisam ser tratadas de acordo com o nível de confiança do código em checkout. Workflows não devem usar esta Action para inspecionar código não confiável em um contexto privilegiado, como um workflow com acesso a segredos, sem uma revisão explícita de segurança.

## Alternativas consideradas

### Action JavaScript/TypeScript que invoca a CLI

Uma JavaScript Action pode executar diretamente em runners Linux, Windows e macOS e normalmente possui baixo overhead de inicialização do wrapper. Ela não foi escolhida porque a inspeção real ainda exigiria o runtime .NET do Inspector ou um artefato nativo distribuído separadamente.

Usar JavaScript apenas para baixar e invocar a mesma CLI adicionaria outro build/runtime/toolchain, grafo de dependências, artefato `dist` empacotado e superfície de release sem remover a exigência de execução .NET nem fornecer uma capacidade de inspeção que a Composite Action não possa fornecer.

Um wrapper JavaScript poderá ser reconsiderado se comportamentos específicos da Action se tornarem complexos o suficiente para justificar uma camada de implementação dedicada, mas a lógica de inspeção deverá continuar na engine/CLI .NET.

### Docker container Action

Uma Docker Action oferece consistência forte de empacotamento e pode incluir todas as dependências de runtime. Ela foi rejeitada como Action principal porque Docker container actions do GitHub executam apenas em runners Linux e adicionam overhead de download/inicialização de imagem.

Isso conflita diretamente com o objetivo de compatibilidade com Windows/macOS do projeto.

### Binários self-contained anexados às releases

Publicar binários self-contained do Inspector removeria o pré-requisito do runtime .NET para a Action e poderia reduzir o trabalho de bootstrap depois do download.

Essa opção não foi escolhida inicialmente porque cria uma matriz de release por sistema operacional e arquitetura, exige seleção e validação de integridade de assets, aumenta o tamanho dos artefatos e cria uma segunda rota de distribuição ao lado do pacote .NET Tool já validado.

A opção permanece aberta se o custo medido de inicialização da Action justificar a complexidade adicional de release.

### Composite Action exigindo que o consumidor pré-instale o Inspector

Exigir que o consumidor instale uma versão específica da Tool antes da Action minimizaria o trabalho de implementação da Action, mas tornaria o step reutilizável incompleto e fácil de configurar incorretamente. Isso também enfraqueceria a relação entre uma release da Action e a versão do Inspector que realmente executa.

O design selecionado, portanto, é responsável pelo bootstrap do Inspector, mantendo explícita no workflow consumidor a instalação dos SDKs específicos do repositório.

## Consequências

### Positivas

- A Action reutiliza exatamente a mesma CLI e engine da execução local.
- Nenhum comportamento de classificação ou inspeção é duplicado em YAML, shell, PowerShell ou JavaScript.
- O modelo de distribuição suporta runners Linux, Windows e macOS.
- Refs de release da Action selecionam deterministicamente uma versão do Inspector e uma fronteira de compatibilidade do schema.
- O repositório inspecionado não é modificado pela instalação da Tool.
- Uma inspeção normal não exige token do GitHub nem permissão de escrita.
- A fronteira de package source reduz o risco de dependency confusion/shadowing durante o bootstrap da Tool.

### Trade-offs

- O primeiro uso poderá precisar instalar um runtime/SDK .NET compatível e baixar o pacote da Tool.
- A disponibilidade do pacote NuGet passa a fazer parte da disponibilidade da Action.
- O consumidor ainda precisa disponibilizar os SDKs selecionados pelo repositório quando eles não estiverem instalados.
- O glue cross-platform da Composite Action precisa ser testado em todos os sistemas operacionais de runner suportados.
- Aliases da Action como `v1` exigem disciplina no gerenciamento de releases/tags.

## Fronteira de implementação

A issue #14 implementará `action.yml`, o glue cross-platform de bootstrap/invocação, os testes de CI da Action e a documentação de uso para usuários de acordo com esta ADR.

Mudar do modelo selecionado de Composite/.NET Tool, permitir overrides arbitrários da versão da Tool ou alterar a relação entre versões major da Action/schema exige uma nova ADR ou decisão explícita que substitua esta.

## Referências

- GitHub Docs — About custom actions: https://docs.github.com/actions/concepts/workflows-and-actions/custom-actions
- GitHub Docs — Creating a composite action: https://docs.github.com/actions/tutorials/create-actions/create-a-composite-action
- GitHub Docs — Metadata syntax for GitHub Actions: https://docs.github.com/actions/reference/workflows-and-actions/metadata-syntax
- GitHub Docs — Managing custom actions: https://docs.github.com/actions/how-tos/create-and-publish-actions/manage-custom-actions
- GitHub Docs — Releasing and maintaining actions: https://docs.github.com/actions/how-tos/create-and-publish-actions/release-and-maintain-actions
- Microsoft Learn — `dotnet tool install`: https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-install
- [ADR 0001: Avaliar projetos por meio de `dotnet msbuild`](0001-msbuild-evaluation-strategy.md)
