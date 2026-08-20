# Contribuindo com o DotNetRepoInspector

**Idiomas:** [English](CONTRIBUTING.md) | Português (Brasil)

Obrigado por considerar uma contribuição. O DotNetRepoInspector é uma infraestrutura orientada a automação, portanto mudanças devem ser pequenas, reproduzíveis e explícitas sobre qualquer impacto em contratos públicos.

Ao participar deste projeto, você concorda em seguir o [Código de Conduta](CODE_OF_CONDUCT.pt-BR.md). Para vulnerabilidades suspeitas, **não** abra uma issue pública; siga o [SECURITY.md](SECURITY.md).

## Antes de começar

Para bugs e melhorias focadas, uma issue é recomendada quando ajuda a estabelecer um problema reproduzível ou o comportamento esperado. Para mudanças arquiteturais amplas, novos contratos públicos, novas estratégias de persistência ou comportamento que possa afetar compatibilidade, abra ou referencie uma issue antes de investir em uma implementação grande.

Mantenha pull requests focados. Evite misturar refactors não relacionados com mudanças de comportamento.

## Ambiente de desenvolvimento

Você precisa de:

- Git;
- um SDK .NET 10 compatível com o [`global.json`](global.json) (`10.0.100` com roll-forward `latestFeature`);
- Python 3 apenas se quiser executar o mesmo script local de resumo de cobertura usado pelo CI.

Nenhum banco de dados, conta de cloud, serviço HTTP público ou token do GitHub é necessário para o ciclo normal de build/teste.

Clone o repositório e valide o SDK selecionado:

```bash
git clone https://github.com/rodri-oliveira-dev/DotNetRepoInspector.git
cd DotNetRepoInspector
dotnet --version
```

## Build e testes locais

A validação baseline é:

```bash
dotnet restore ./DotNetRepoInspector.slnx

dotnet format ./DotNetRepoInspector.slnx \
  --verify-no-changes \
  --no-restore \
  --severity warn

dotnet build ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-restore \
  --warnaserror \
  -p:RunAnalyzers=true

dotnet test \
  --solution ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-build \
  --no-restore
```

Para reproduzir a execução de cobertura do CI, use:

```bash
dotnet test \
  --solution ./DotNetRepoInspector.slnx \
  --configuration Release \
  --no-build \
  --no-restore \
  --results-directory ./artifacts/test-results \
  -- \
  --report-trx \
  --coverlet \
  --coverlet-output-format cobertura

python ./.github/scripts/coverage_summary.py \
  --reports "artifacts/test-results/**/*cobertura*.xml" \
  --baseline ./.github/coverage-baseline.json
```

Durante a implementação, execute primeiro os testes mais próximos da mudança e, antes de abrir o pull request, execute a baseline do repositório.

## Convenções do projeto

Siga o [`AGENTS.md`](AGENTS.md) para limites arquiteturais, regras de inspeção, estratégia de testes, contratos públicos, gerenciamento de dependências, sincronização de documentação e expectativas de validação.

Regras importantes para contribuições incluem:

- metadados MSBuild avaliados são a principal fonte da verdade;
- `Core` deve permanecer independente de infraestrutura;
- a inspeção permanece somente leitura e evita acesso à rede por padrão;
- não colete nem registre secrets, credenciais, valores de ambiente ou conteúdo arbitrário de código-fonte;
- dependências usam Central Package Management;
- documentação pública nas árvores em inglês e português deve permanecer sincronizada;
- use prefixos de Conventional Commits como `feat:`, `fix:`, `test:`, `docs:`, `ci:`, `refactor:` ou `chore:`.

## Adicionando ou alterando classificações de projeto

Mudanças de classificação exigem evidência reproduzível, não intuição baseada em nomes.

Uma contribuição que adiciona um sinal de classificação, altera precedência ou corrige um bug de classificação deve:

1. adicionar ou atualizar uma **fixture sintética mínima** em `tests/Fixtures/` que reproduza o estado MSBuild avaliado relevante;
2. adicionar um teste de regressão que falhe sem o comportamento proposto;
3. preferir properties/items/imports avaliados a nomes de projeto, nomes de diretório ou convenções de arquivos de código-fonte;
4. preservar precedência determinística e preferir `Unknown` quando a evidência for insuficiente;
5. atualizar documentação de classificação/schema/diagnósticos nos dois idiomas quando o comportamento público mudar.

Um repositório público real pode ser usado para descobrir ou explicar um bug, mas o teste permanente de regressão deve ser reduzido a uma pequena fixture local. Não faça a suíte normal depender de acesso à rede ou de repositórios externos mutáveis.

## Adicionando ou alterando agent skills

As skills do repositório ficam em `.agents/skills/<skill-name>/`.

Uma skill nova ou alterada materialmente deve:

- ter escopo focado e reutilizável, em vez de se tornar um conjunto genérico de instruções;
- usar diretório em kebab-case e um `SKILL.md` com front matter `name` e `description` precisos;
- declarar claramente quando deve e quando não deve ser utilizada;
- permanecer subordinada ao [`AGENTS.md`](AGENTS.md), sem duplicar ou contradizer regras do repositório;
- evitar informações específicas de usuário, credenciais, detalhes privados de organização ou secrets de ambiente;
- incluir orientação de validação/conclusão apropriada ao escopo;
- atualizar `.agents/skills/THIRD-PARTY-NOTICES.md` quando copiar ou adaptar material de terceiros que exija atribuição ou preservação de aviso.

Se uma skill afetar CI, release, segurança ou contratos públicos, a documentação e os testes/gates do repositório continuam sendo a fonte da verdade; uma skill não deve redefinir silenciosamente o comportamento do produto.

## Contratos públicos e compatibilidade

Trate schema JSON, diagnósticos, flags/códigos de saída da CLI, empacotamento da .NET Tool, inputs/outputs da GitHub Action e semântica do envelope de persistência como contratos de produto.

Quando uma contribuição alterar uma dessas superfícies:

- destaque o impacto de compatibilidade no pull request;
- adicione ou atualize testes de contrato;
- atualize em conjunto a documentação correspondente em inglês e português;
- prefira mudanças aditivas dentro da mesma major do schema;
- use uma ADR quando houver uma decisão arquitetural duradoura.

## Documentação

`README.md` e `README.pt-BR.md` são pontos de entrada do projeto. A documentação pública detalhada fica em `docs/en/` e `docs/pt-BR/` com caminhos relativos equivalentes.

Ao alterar documentação pública, atualize os dois idiomas no mesmo pull request. Identificadores técnicos, comandos, propriedades JSON, códigos de diagnóstico e nomes de API não devem ser traduzidos.

## Pull requests

Antes de solicitar revisão:

- confirme que a mudança está limitada a um único objetivo;
- execute formatação, build/analyzers e testes relevantes;
- adicione fixtures/testes de regressão para mudanças de comportamento da inspeção;
- atualize documentação e testes de contratos públicos quando aplicável;
- confirme que nenhum secret, credencial, output gerado de build ou arquivo não relacionado foi incluído.

O template de pull request é deliberadamente condicional: marque os itens aplicáveis e explique qualquer validação que não tenha sido possível executar localmente.

## Segurança e relatos sensíveis

Não inclua credenciais reais, conteúdo de repositório privado, detalhes sensíveis de exploit, headers de autorização, connection strings ou dados de clientes em issues, fixtures, testes, logs ou pull requests.

Para vulnerabilidades de segurança, siga o [SECURITY.md](SECURITY.md) e use um canal privado em vez dos templates públicos de issue.

## Licença

Ao enviar uma contribuição, você concorda que ela será licenciada sob a [Licença MIT](LICENSE) do projeto.
