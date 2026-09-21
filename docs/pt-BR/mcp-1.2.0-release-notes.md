# DotNetRepoInspector 1.2.0 e servidor MCP

> Release notes preliminares. A versão `1.2.0` e o `DotNetRepoInspector.Mcp` não estão publicados. Este documento deve permanecer preliminar até a aprovação do workflow protegido de GA e dos gates pós-publicação.

## Destaques

- Novo servidor local e read-only `DotNetRepoInspector.Mcp` sobre stdio.
- Seis tools determinísticas apoiadas pela Engine existente e por fatos avaliados de MSBuild/Git.
- Fronteira explícita de repository root com canonicalização de paths e defesas contra traversal/links.
- Empacotamento framework-dependent como `McpServer` NuGet e .NET Tool, com suporte a versão exata via `dnx`.
- Caminho protegido de release OIDC/Trusted Publishing com validação de pacotes, hashes, attestations, manifest e smoke pós-publicação.
- Dataset versionado de evals determinísticos e evidências de compatibilidade de clientes, sem SDKs de providers no código do produto.

## Catálogo estável de tools MCP

`inspect_repository`, `list_projects`, `get_project_details`, `get_project_reference_graph`, `get_repository_diagnostics` e `get_sdk_metadata` formam o catálogo v1 congelado. Todas são read-only e usam stdio com `--root` explícito.

## Segurança e limitações

A avaliação MSBuild não é sandbox. Repositórios não confiáveis devem ser inspecionados somente em ambientes isolados, efêmeros, com privilégio mínimo e sem credenciais. O servidor não chama uma LLM; o tratamento posterior dos dados segue a política do cliente MCP. HTTP remoto, tools de escrita, RAG, embeddings e comportamento de runtime específico por provider não fazem parte desta release.

## Instalação após a publicação

```bash
dnx DotNetRepoInspector.Mcp@1.2.0 --yes -- --root /caminho/absoluto/para/o/repositorio
```

Este comando é deliberadamente prospectivo até a publicação no NuGet.org ser verificada. Consulte o [readiness do GA](mcp-ga-readiness.md) antes de apresentá-lo como disponível.

## Estado de compatibilidade

OpenAI Codex possui evidência com cliente real contra o servidor de desenvolvimento. Claude Code e Gemini CLI possuem configurações reproduzíveis, mas nenhuma evidência de validação do projeto. O GA exige um segundo provider contra o pacote exato publicado.
