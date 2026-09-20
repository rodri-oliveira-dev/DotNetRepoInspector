# Readiness do release candidate MCP

**Idiomas:** [English](../en/mcp-release-candidate.md) | Português (Brasil)

Este relatório define e registra o trabalho de release candidate da issue #137. A identidade escolhida é **`1.2.0-rc.1`**: a última release pública do produto é `1.1.0`, e a distribuição MCP adiciona uma capacidade retrocompatível, portanto a próxima versão em lockstep é um prerelease minor.

## Specification

O RC deve usar o workflow protegido de release e a mesma versão exata para CLI, pacote MCP, GitHub Action, container, manifest e tag de release. A entrada exige #133, #134, #135, #136 e #139 concluídas; gates do repositório verdes; nenhuma issue crítica de segurança MCP sem tratamento; e o baseline aplicável da #106. A promoção para GA é proibida até que o pacote publicado seja resolvido por versão exata no NuGet.org e pelo menos dois clientes MCP reais de providers diferentes sejam aprovados usando esse pacote.

As evidências devem distinguir build local, pacote resolvido de um feed local controlado, pacote publicado no NuGet.org e execução por cliente real de provider. Assertions determinísticas de protocolo não substituem evidência multi-provider.

## Plan

1. Validar issues de entrada e estado de segurança antes de habilitar publicação.
2. Compilar, testar, empacotar, inspecionar metadados e executar `1.2.0-rc.1` a partir de feed controlado.
3. Executar o dataset de fixtures, a solução do repositório e cenários de erro conhecidos pelo servidor empacotado.
4. Executar o workflow protegido com `publish=false` e preservar manifest, checksums e pacotes.
5. Somente após todos os gates de entrada e a policy administrativa de Trusted Publishing serem confirmados, disparar `publish=true` de uma ref permitida e obter a aprovação do environment.
6. Resolver a versão publicada exata com `dnx`, repetir protocolo/evals e validar pelo menos dois providers antes da promoção.

## Tasks e evidências

| Gate | Evidência exigida | Estado atual |
| --- | --- | --- |
| Pacote e protocolo | Pacote prerelease exato, validação de metadados, handshake, `tools/list` e `inspect_repository` | Aprovado em feed controlado para `1.2.0-rc.1` |
| Evals determinísticos | Sete casos versionados de fixtures, incluindo SDK ausente e referência não resolvida | Aprovado 7/7 via `dnx` com versão exata |
| Repositório real | Solução do repositório inspecionada pelo servidor empacotado | Aprovado; projeto MCP reportou `net10.0` |
| Multi-cliente | Dois clientes reais de providers usando o pacote publicado | **Bloqueado:** somente OpenAI Codex possui evidência real anterior |
| Publicação | Versão exata disponível no NuGet.org | **Bloqueado:** o PackageId ainda não possui versões publicadas |
| Supply chain | Release manifest, SHA-256, attestations e SBOM/provenance do container | Preparado pelo workflow protegido; evidência oficial exige execução de publicação |

## Findings

| ID | Severidade | Impacto e reprodução | Resolução exigida |
| --- | --- | --- | --- |
| `RC-001` | Bloqueador | A issue #133 permanece aberta porque Claude Code e Gemini CLI estão indisponíveis no ambiente; somente um provider foi comprovado. | Validar o RC publicado exato com Claude Code ou Gemini CLI e anexar evidência não sensível da chamada. |
| `RC-002` | Bloqueador | A issue #139 permanece aberta porque a suíte de evals não foi executada por dois providers. | Executar a suíte versionada com um segundo provider e comparar fatos estruturados, seleção de tool, groundedness e afirmações não suportadas. |
| `RC-003` | Bloqueador | A issue #106 permanece aberta, enquanto o workflow oficial publica o container em lockstep e exige seu baseline de release. | Concluir ou resolver explicitamente os critérios aplicáveis de readiness da #106 antes da aprovação. |
| `RC-004` | Bloqueador | `DotNetRepoInspector.Mcp` não retorna versões no endpoint flat-container do NuGet.org. Não existem evidências de pacote publicado, página, provenance ou smoke pós-publicação. | Configurar/ativar a policy de Trusted Publishing e realizar a publicação prerelease autorizada pelo workflow protegido. |
| `RC-005` | Média | O baseline v1 machine-readable ainda identifica o baseline histórico `1.0.0`, enquanto as releases públicas avançaram para `1.1.0`. Ele permanece válido como contrato v1, mas não é a versão do RC. | Manter o baseline imutável e registrar separadamente a identidade do RC, como feito em `mcp.releaseCandidate`. |

## Estado da implementação

O repositório registra `1.2.0-rc.1` como **bloqueado**, não publicado. `.github/scripts/validate_mcp_rc.ps1` valida conteúdo do pacote, instalação local da tool, resolução exata via `dnx` em feed controlado, handshake stdio, discovery, `inspect_repository` e todos os casos determinísticos. `.github/release-readiness-v1.json` lista as issues #106, #133 e #139 e a policy externa de Trusted Publishing como bloqueios.

Execução em feed controlado em 2026-09-20: 7/7 casos de eval e 1/1 caso no repositório real passaram com 100% de task completion, tool selection, fidelidade factual e groundedness; chamadas desnecessárias e afirmações não suportadas foram zero. Os cenários de erro verificaram `DRI1002` e `DRI1003`. Evidências SHA-256 locais foram registradas para pacote CLI, pacote MCP e símbolos MCP. Esses resultados comprovam o pacote gerado, não publicação no NuGet.org ou compatibilidade com clientes reais.

Nenhum workflow com `publish=true` pode ser disparado enquanto esses bloqueios permanecerem. Depois de resolvidos, o workflow de release é o único caminho autorizado de publicação. Consulte [engenharia de release](releases.md), [compatibilidade de clientes e evals](mcp-agent-compatibility.md) e [operações MCP](mcp.md).
