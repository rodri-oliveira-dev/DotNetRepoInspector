# Readiness da release v1.0.0

**Idiomas:** [English](../en/v1-release-readiness.md) | Português (Brasil)

O DotNetRepoInspector está sendo preparado para sua primeira release pública estável como **v1.0.0**. Este documento define a baseline de readiness que deve continuar verdadeira antes que o workflow protegido de publicação possa entregar a release.

Este documento **não** significa que a `v1.0.0` já foi publicada. Até o workflow protegido de Release terminar com sucesso, o pacote NuGet e as tags públicas da GitHub Action ainda podem não estar disponíveis.

## Baseline da v1

| Superfície | Baseline v1.0.0 |
| --- | --- |
| Versão do produto | `1.0.0` |
| Schema de inspeção | `1.3` (major `1`) |
| Pacote NuGet | `DotNetRepoInspector` |
| Comando da .NET Tool | `dotnet-repo-inspect` / `dotnet repo-inspect` |
| Runtime da Tool | `net10.0` |
| Alias estável da GitHub Action | `v1` |
| Tag imutável da GitHub Action | `v1.0.0` |
| Alias minor da GitHub Action | `v1.0` |
| Licença | MIT |

A contraparte legível por máquina desta tabela é `.github/release-readiness-v1.json`. Testes do repositório comparam essa baseline com `action.yml`, `InspectionSchema`, os metadados do pacote da CLI, o exemplo canônico do schema e os arquivos obrigatórios de governança/segurança.

## Contrato público incluído na v1

A primeira release estável inclui estas superfícies suportadas:

- descoberta de repositórios/projetos baseada em metadados .NET/MSBuild avaliados;
- classificação base: Web, Worker, Console, Library, Test e Unknown;
- referências normalizadas entre projetos e metadados Git do repositório;
- JSON de inspeção versionado, tendo `schemaVersion 1.3` como baseline da primeira release v1;
- `.dotnetrepoinspector.json` opcional, exclusões e overrides explícitos de classificação;
- CLI/.NET Tool com separação determinística entre stdout/stderr e códigos de saída documentados;
- Composite GitHub Action reutilizando a mesma .NET Tool;
- persistência HTTP/webhook opcional de snapshots com proveniência e idempotência;
- validação de compatibilidade para repositórios alvo .NET 8/10 em Ubuntu, Windows e macOS;
- fronteiras de segurança/privacidade, governança OSS, validação em repositórios reais e guardrails de performance.

Subtipos de aplicações e a camada opcional de políticas permanecem como trabalho pós-v1. Eles não fazem parte da promessa de compatibilidade da v1.0.0.

## Limites de compatibilidade

O produto segue a política de versionamento em [`releases.md`](releases.md).

Para a linha v1:

- mudanças aditivas do contrato de inspeção podem avançar o schema `1.x` e exigem uma release MINOR apropriada do produto;
- mudanças breaking do schema de inspeção exigem schema major `2` e, portanto, uma nova major do produto/Action em vez de mover o alias `v1`;
- mudanças breaking da CLI ou dos inputs/outputs da GitHub Action também exigem nova major do produto;
- um alias estável `v1` nunca pode apontar para uma release cujo contrato público pertença à major `2` do produto.

O exemplo JSON canônico atual é [`schema/examples/inspection-v1.example.json`](schema/examples/inspection-v1.example.json).

## Gate automatizado de readiness

`tests/DotNetRepoInspector.Cli.Tests/ReleaseReadinessTests.cs` valida a baseline v1 dentro da suíte normal de testes. Como `Validate .NET` e o workflow protegido de Release executam a suíte completa, o mesmo gate é aplicado no CI comum e nos dry-runs de release.

O gate verifica:

1. a versão do produto em `.github/release-readiness-v1.json` define a baseline inicial da v1, enquanto as versões oficiais são informadas e validadas pelo workflow de Release;
2. major do produto, alias major da Action e major do schema de inspeção estão alinhados para a baseline v1;
3. `InspectionSchema.CurrentVersion` é exatamente a versão de schema da baseline;
4. o projeto da CLI continua sendo uma .NET Tool empacotável com package ID, comando, target framework, licença, README e repository URL esperados;
5. o exemplo canônico de schema anuncia o mesmo `schemaVersion`;
6. arquivos obrigatórios de licença, segurança, contribuição, conduta, templates de issue/PR e documentação de releases existem;
7. os READMEs públicos não contêm mais mensagens pré-v1 que descrevam o schema como hipotético ou não definitivo.

Esse gate não valida configurações externas das contas GitHub/NuGet.org; elas permanecem como pré-requisitos administrativos.

## Verificações no repositório antes da publicação

Antes de iniciar a release oficial, confirme na `main`:

- `Validate .NET` está verde;
- o dry-run do workflow Release está verde para `1.0.0`;
- build/analyzers possuem zero warnings e erros;
- a suíte completa de testes passa;
- a validação do pacote instala o `DotNetRepoInspector.1.0.0.nupkg` exato global e localmente e verifica `--help`, `--version` e uma inspeção real;
- o release candidate contém `release-manifest.json` e `SHA256SUMS`;
- o manifest aponta para o commit exato da release e informa schema `1.3`;
- smoke tests da GitHub Action e de compatibilidade estão verdes em Ubuntu, Windows e macOS.

Um dry-run manual seguro pode ser iniciado em **Actions → Release → Run workflow**, versão `1.0.0`, `publish=false`. O job de publicação deve ser ignorado.

## Pré-requisitos administrativos da primeira publicação

Estes passos ficam intencionalmente fora do código do repositório e devem ser concluídos por um maintainer antes que `publish=true` possa funcionar:

1. Criar um GitHub Environment `release` protegido.
2. Exigir aprovação nesse environment e restringir deployment à `main` conforme apropriado para o repositório.
3. Definir `NUGET_USER` como variável do repositório/environment com a conta NuGet.org usada na publicação.
4. No NuGet.org, configurar **Trusted Publishing** para o pacote `DotNetRepoInspector`, este repositório GitHub, `.github/workflows/release.yml` e, preferencialmente, o environment `release`.
5. Confirmar que o package ID está disponível/pertence à conta NuGet pretendida antes da primeira publicação.

Nenhuma API key NuGet de longa duração deve ser adicionada ao GitHub Secrets. O workflow usa OIDC/Trusted Publishing.

## Publicando a v1.0.0

Depois que este trabalho de readiness for mergeado e os pré-requisitos administrativos acima estiverem configurados:

1. abra **Actions → Release → Run workflow** na `main`;
2. informe a versão `1.0.0`;
3. defina `publish=true`;
4. aprove o GitHub Environment `release` protegido;
5. deixe o workflow publicar exatamente o candidate construído na mesma execução.

O workflow deriva automaticamente a tag `v1.0.0` da versão `1.0.0`. O workflow protegido é responsável por criar a release/tag imutável `v1.0.0`, publicar o pacote NuGet, publicar a GitHub Release, gerar attestations e somente então mover os aliases estáveis da Action `v1` e `v1.0`.

## Verificação pós-publicação

Depois que o workflow terminar com sucesso, valide independentemente:

```bash
dotnet tool install --global DotNetRepoInspector --version 1.0.0
dotnet repo-inspect --version
```

A versão exibida deve ser `1.0.0`.

Também valide:

- a GitHub Release `v1.0.0` está pública e aponta para o commit pretendido;
- os assets da release contêm pacote, manifest e checksums;
- provenance/attestations estão presentes;
- `v1` e `v1.0` resolvem para o mesmo commit de `v1.0.0`;
- um workflow descartável consegue executar `uses: rodri-oliveira-dev/DotNetRepoInspector@v1`;
- a página do pacote NuGet apresenta licença MIT, README, link do repositório e versão corretos.

Somente depois dessas verificações a roadmap #30 deve ser considerada como tendo atendido seu critério final de “release versionada e reproduzível”.

## Falha parcial

A publicação da GitHub Release, a publicação no NuGet e a movimentação dos aliases da Action não formam uma transação. Se o workflow protegido falhar depois de alguma etapa irreversível, siga as regras de recuperação em [`releases.md`](releases.md). Nunca redirecione a tag imutável `v1.0.0` para outro commit.
