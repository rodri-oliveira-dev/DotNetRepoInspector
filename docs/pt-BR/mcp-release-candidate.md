# Readiness do release candidate MCP

**Idiomas:** [English](../en/mcp-release-candidate.md) | Português (Brasil)

Este relatório define e registra o trabalho de release candidate da issue #137. A identidade escolhida é **`1.2.0-rc.1`**: a última release pública do produto é `1.1.0`, e a distribuição MCP adiciona uma capacidade retrocompatível, portanto a próxima versão em lockstep é um prerelease minor.

## Specification

O RC deve usar o workflow protegido de release e a mesma versão exata para CLI, pacote MCP, GitHub Action, container, manifest e tag de release. A entrada exige #134, #135 e #136 concluídas; gates do repositório específicos do MCP verdes; e nenhuma issue crítica de segurança MCP sem tratamento. O release-readiness de container acompanhado pela #106 é independente e não bloqueia o release candidate do MCP. O trabalho de compatibilidade multi-provider das #133 e #139 é evidência complementar de interoperabilidade e não bloqueia RC nem GA. A promoção para GA exige que o pacote publicado seja resolvido por versão exata no NuGet.org e passe pelas validações determinísticas de protocolo, pacote e smoke.

As evidências devem distinguir build local, pacote resolvido de um feed local controlado, pacote publicado no NuGet.org, validação determinística do protocolo MCP e validação em cliente real quando disponível. Execuções multi-provider continuam úteis como evidência de compatibilidade, mas não são gate de publicação.

## Plan

1. Validar issues de entrada e estado de segurança antes de habilitar publicação.
2. Compilar, testar, empacotar, inspecionar metadados e executar `1.2.0-rc.1` a partir de feed controlado.
3. Executar o dataset de fixtures, a solução do repositório e cenários de erro conhecidos pelo servidor empacotado.
4. Executar o workflow protegido com `publish=false` e preservar manifest, checksums e pacotes.
5. Somente após todos os gates de entrada e a policy administrativa de Trusted Publishing serem confirmados, disparar `publish=true` de uma ref permitida e obter a aprovação do environment.
6. Resolver a versão publicada exata com `dnx`, repetir protocolo/evals e o smoke com Codex, registrando validações adicionais de providers quando estiverem disponíveis.

## Tasks e evidências

| Gate | Evidência exigida | Estado atual |
| --- | --- | --- |
| Pacote e protocolo | Pacote prerelease exato, validação de metadados, handshake, `tools/list` e `inspect_repository` | Aprovado em feed controlado para `1.2.0-rc.1` |
| Evals determinísticos | Sete casos versionados de fixtures, incluindo SDK ausente e referência não resolvida | Aprovado 7/7 via `dnx` com versão exata |
| Repositório real | Solução do repositório inspecionada pelo servidor empacotado | Aprovado; projeto MCP reportou `net10.0` |
| Multi-cliente | Evidência de interoperabilidade com cliente real | **Não bloqueante:** OpenAI Codex está validado; Claude Code e Gemini CLI permanecem como validações opcionais futuras |
| Publicação | Versão exata disponível no NuGet.org | **Bloqueado:** o PackageId ainda não possui versões publicadas |
| Supply chain | Release manifest, SHA-256, attestations e SBOM/provenance do container | Preparado pelo workflow protegido; evidência oficial exige execução de publicação |

## Findings

| ID | Severidade | Impacto e reprodução | Resolução exigida |
| --- | --- | --- | --- |
| `RC-001` | Bloqueador | `DotNetRepoInspector.Mcp` não retorna versões no endpoint flat-container do NuGet.org. Não existem evidências de pacote publicado, página, provenance ou smoke pós-publicação. | Configurar/ativar a policy de Trusted Publishing e realizar a publicação prerelease autorizada pelo workflow protegido. |
| `RC-002` | Média | O baseline v1 machine-readable ainda identifica o baseline histórico `1.0.0`, enquanto as releases públicas avançaram para `1.1.0`. Ele permanece válido como contrato v1, mas não é a versão do RC. | Manter o baseline imutável e registrar separadamente a identidade do RC, como feito em `mcp.releaseCandidate`. |

## Estado da implementação

O repositório registra `1.2.0-rc.1` como **bloqueado**, não publicado. `.github/scripts/validate_mcp_rc.ps1` valida conteúdo do pacote, instalação local da tool, resolução exata via `dnx` em feed controlado, handshake stdio, discovery, `inspect_repository` e todos os casos determinísticos. `.github/release-readiness-v1.json` não possui blocker de issue para o RC; a policy externa de Trusted Publishing permanece como bloqueio de publicação. As issues #106, #133 e #139 continuam acompanhadas separadamente como readiness de container e evidência adicional de interoperabilidade.

Execução em feed controlado em 2026-09-20: 7/7 casos de eval e 1/1 caso no repositório real passaram com 100% de task completion, tool selection, fidelidade factual e groundedness; chamadas desnecessárias e afirmações não suportadas foram zero. Os cenários de erro verificaram `DRI1002` e `DRI1003`. Evidências SHA-256 locais foram registradas para pacote CLI, pacote MCP e símbolos MCP. Esses resultados comprovam o pacote gerado, não publicação no NuGet.org ou compatibilidade com clientes reais.

Nenhum workflow com `publish=true` pode ser disparado enquanto esses bloqueios permanecerem. Depois de resolvidos, o workflow de release é o único caminho autorizado de publicação. Consulte [engenharia de release](releases.md), [compatibilidade de clientes e evals](mcp-agent-compatibility.md) e [operações MCP](mcp.md).
