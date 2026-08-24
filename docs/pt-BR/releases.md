# Releases e versionamento

**Idiomas:** [English](../en/releases.md) | Português (Brasil)

O DotNetRepoInspector usa uma única versão de produto para a .NET Tool e para a GitHub Action reutilizável. A publicação oficial é deliberadamente separada do CI normal e acontece somente pelo workflow protegido `Release`.

## Versão do produto

As releases do produto seguem [Semantic Versioning 2.0.0](https://semver.org/):

- **PATCH** corrige comportamento sem quebrar contrato público;
- **MINOR** adiciona comportamento ou capacidades de contrato de forma retrocompatível;
- **MAJOR** é obrigatória para mudanças breaking na CLI, Action ou contrato de inspeção.

Identificadores de prerelease como `1.1.0-rc.1` são suportados. Build metadata (`+...`) não é aceito em releases oficiais para que versão NuGet, tag Git, título da release e versão da Action permaneçam idênticos e sem ambiguidade.

A versão exata preparada por um commit é o valor fixado em `DRI_TOOL_VERSION` no `action.yml` da raiz. O workflow de release rejeita uma versão solicitada diferente desse pin. Assim, uma revisão publicada da Action e a versão do pacote `DotNetRepoInspector` que ela instala permanecem inseparáveis.

## Versão do produto versus `schemaVersion`

A versão do produto e o `schemaVersion` do JSON de inspeção são contratos de compatibilidade relacionados, mas não são o mesmo número.

- correções de implementação que não alteram o contrato JSON podem ser PATCH do produto mantendo `schemaVersion`;
- mudanças aditivas e retrocompatíveis do contrato incrementam a minor do schema e exigem pelo menos uma MINOR do produto;
- uma quebra do contrato de inspeção incrementa a major do schema e exige uma MAJOR do produto;
- uma quebra de inputs/outputs da GitHub Action também exige MAJOR do produto/Action, mesmo que o schema JSON não mude.

Uma tag móvel de major como `v1` nunca pode atravessar de schema major `1` para schema major `2`.

## Tags da GitHub Action

Para uma release estável `1.4.2`, a publicação usa:

- tag completa e imutável `v1.4.2`;
- alias móvel de major `v1`;
- alias móvel de minor `v1.4`.

A tag imutável identifica o commit exato da release. Os aliases móveis são atualizados somente depois que o pacote NuGet exato foi publicado e a GitHub Release está pronta para publicação.

Prereleases como `1.5.0-rc.1` recebem somente a tag completa imutável. Elas nunca movem aliases estáveis de major/minor.

Consumidores que priorizam conveniência podem usar `@v1`; quem prioriza máxima reprodutibilidade deve usar `@v1.4.2` ou um SHA completo de commit.

## Artifacts de release

Todo build de release produz o mesmo conjunto validado antes da publicação:

- `DotNetRepoInspector.<version>.nupkg`;
- `release-manifest.json`;
- `SHA256SUMS`.

O manifest registra:

- versão do produto e tag imutável;
- SHA completo de 40 caracteres do commit de origem;
- `schemaVersion` observado em uma inspeção real feita pela Tool empacotada;
- SHA-256 do `.nupkg`;
- aliases da Action elegíveis para movimentação naquela versão.

O build passa o pacote pelo validador existente da .NET Tool, incluindo metadata/conteúdo, instalação global e local, `--help`, `--version` e inspeção real de repositório. Portanto, o pacote é validado antes de poder entrar no job de publicação.

## Pull requests normais

A validação normal nunca possui credenciais de publicação nem permissões de escrita para release.

Quando a própria automação de release/packaging muda, `.github/workflows/release.yml` também executa no pull request em **modo dry-run**. Esse caminho faz restore, validação de formatação, build, testes, pack da versão exata, smoke do pacote, manifest/checksums e upload do artifact. O job de publicação fica skipped porque só pode executar em `workflow_dispatch` explícito com `publish=true`.

O workflow normal `Validate .NET` continua gerando seu `.nupkg` versionado para CI de forma independente e nunca o publica.

## Configuração da publicação protegida

Antes da primeira release oficial, mantenedores devem configurar um GitHub Environment chamado **`release`**. Proteções recomendadas:

1. exigir aprovação de pelo menos um mantenedor;
2. restringir branches/tags de deployment para que a publicação seja iniciada somente a partir da `main`;
3. definir a variável de environment/repositório `NUGET_USER` com o nome da conta NuGet.org usada pelo Trusted Publishing.

O NuGet.org também deve possuir uma policy de Trusted Publishing para o pacote `DotNetRepoInspector` confiando neste repositório, no workflow `release.yml` e, preferencialmente, no environment `release`.

Nenhuma API key de longa duração do NuGet pertence a GitHub Secrets. O job de publicação solicita uma identidade OIDC e `NuGet/login` a troca por uma API key temporária.

## Iniciar uma release oficial

Publicar é intencionalmente uma ação de mantenedor, não um efeito colateral de fazer merge de um PR.

1. Escolha a próxima Semantic Version pelas regras de compatibilidade acima.
2. Atualize o pin exato `DRI_TOOL_VERSION` em `action.yml` em um PR revisado. O smoke existente da Action deve continuar alinhado com essa versão.
3. Faça merge somente quando o CI normal e o dry-run de release estiverem verdes.
4. Abra **Actions → Release → Run workflow** na `main`.
5. Informe exatamente a versão já fixada em `action.yml`.
6. Defina `publish` como `true`.
7. Digite exatamente `v<version>`, por exemplo `v1.4.2`, no campo de confirmação.
8. Aprove o environment protegido `release` quando solicitado.

Um workflow dispatch com `publish=false` é um dry-run manual seguro e nunca entra no job de publicação.

## Sequência de publicação

O job protegido ordena deliberadamente as operações irreversíveis:

1. baixa e verifica novamente o artifact exato produzido pelo job de build;
2. gera provenance GitHub/SLSA para pacote, manifest e arquivo de checksums;
3. cria ou retoma uma GitHub Release em **draft** para a tag completa imutável e anexa os artifacts;
4. autentica no NuGet.org via Trusted Publishing/OIDC;
5. publica o `.nupkg` exato com comportamento seguro para duplicidade;
6. publica a GitHub Release;
7. somente em releases estáveis, move `v<major>` e `v<major>.<minor>` para o commit da release.

Essa ordem impede que um alias estável da Action aponte para uma release cujo pacote NuGet não tenha sido publicado com sucesso.

## Falha parcial e reruns

Publicar no GitHub e no NuGet.org não é uma transação única. O workflow oferece, portanto, uma recuperação restrita:

- uma tag completa existente só é aceita se resolver para o mesmo commit da release;
- uma GitHub Release existente só pode ser retomada enquanto ainda for draft;
- assets são reenviados permitindo substituição;
- publicação NuGet usa comportamento seguro para pacote duplicado;
- uma GitHub Release já publicada é considerada imutável e o workflow se recusa a alterá-la;
- aliases estáveis são movidos apenas na etapa final bem-sucedida.

Se uma release parcial exigir reparo humano, verifique o draft, o estado do pacote no NuGet, o commit do manifest e os logs do workflow antes de rerodar. Nunca redirecione uma tag completa imutável para outro commit.

## Release notes e changelog

GitHub Releases são o changelog canônico do produto. `.github/release.yml` categoriza as release notes geradas pelo GitHub em breaking changes, features, fixes, security, documentation, dependencies e outras mudanças. Um PR pode sair das notas geradas usando a label `skip-changelog`.

As notas podem ser editadas enquanto a release ainda é draft. Depois da publicação, a tag completa imutável e as evidências anexadas definem o que foi entregue.

## Provenance e assinatura

Artifacts oficiais recebem GitHub artifact attestations geradas por `actions/attest`, fornecendo provenance de build vinculada à identidade do workflow e aos digests dos artifacts. A publicação NuGet usa OIDC Trusted Publishing em vez de API key armazenada.

Assinatura de autor NuGet baseada em certificado **não** é introduzida pela automação inicial. Ela exige design próprio para ciclo de vida da chave/certificado, renovação e recuperação de incidente. Pode ser adicionada futuramente sem alterar o modelo de versionamento. A evidência atual é formada por tag/commit imutável, manifest/checksums, attestation de provenance do GitHub e identidade do Trusted Publishing do NuGet.

## Permissões

O workflow usa `contents: read` por padrão. Somente o job protegido de publicação recebe permissões adicionais necessárias para criar tags/releases e attestations:

- `contents: write`;
- `id-token: write`;
- `attestations: write`;
- `artifact-metadata: write`.

Código de pull request nunca executa com essas permissões de publicação.

## Documentação relacionada

- [GitHub Action](github-action.md)
- [CLI / .NET Tool](cli.md)
- [Schema de inspeção](schema/inspection-v1.md)
- [Segurança e privacidade](security.md)
- [ADR 0002: estratégia de distribuição da GitHub Action](decisions/0002-github-action-distribution-strategy.md)
