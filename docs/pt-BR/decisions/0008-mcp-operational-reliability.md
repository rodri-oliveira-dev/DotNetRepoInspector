# ADR 0008: Limitar a concorrência de inspeções MCP e a telemetria operacional

**Idiomas:** [English](../../en/decisions/0008-mcp-operational-reliability.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-19
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

Cada tool MCP deriva sua resposta de uma nova chamada a `IRepositoryInspector`. Chamadas concorrentes para a única raiz do servidor poderiam multiplicar processos filhos do Git e MSBuild, disputar os mesmos arquivos e produzir observações de instantes diferentes. Um cache de sessão reduziria trabalho repetido, mas o repositório, a configuração, os imports, a seleção do SDK e o estado do Git podem mudar sem um sinal confiável de invalidação.

O protocolo stdio também exige isolamento do stdout, enquanto operadores precisam correlacionar chamadas lentas, com falha, timeout ou cancelamento sem expor paths do repositório ou inputs das tools.

## Decisão

1. Uma inspeção pode executar por vez para a raiz imutável do servidor. Até oito chamadas adicionais podem aguardar; as seguintes falham com `server_busy`.
2. A espera na fila e a execução da Engine compartilham o token de cancelamento da requisição MCP e um timeout de servidor de cinco minutos. O cancelamento do cliente é propagado; o timeout do servidor retorna `inspection_timed_out`.
3. O cancelamento de processos filhos e o encerramento da árvore de processos continuam sob responsabilidade dos adapters existentes de Git/MSBuild.
4. Cada chamada aceita ou rejeitada emite um evento JSON estruturado de conclusão no stderr com `Tool`, `DurationMs`, `Status` e um `CorrelationId` opaco. Paths, argumentos, texto de exceções e conteúdo de diagnostics não são registrados.
5. Nenhum cache de sessão é introduzido. A inspeção nova é preferida até que um cache limitado demonstre ganho material e tenha invalidação consciente do estado do repositório.
6. Medições MCP reproduzíveis comparam início do processo, startup mais handshake, CLI, Engine e todas as tools v1 usando a fixture controlada `ProjectKinds`. Limites versionados são guardrails de regressão, não um SLA.

## Consequências

O host não pode criar um conjunto ilimitado de avaliações MSBuild simultâneas para uma raiz. Uma inspeção longa causa bloqueio das chamadas seguintes, mas elas continuam canceláveis e a fila limitada protege o uso de processos e memória. Tools ainda podem observar mutações do repositório entre inspeções sequenciais; não existe snapshot ou lock do filesystem implícito.

O timeout de cinco minutos fica intencionalmente acima das baselines controladas atuais. Repositórios grandes que legitimamente precisam de mais tempo continuam mais adequados ao uso direto da Engine/CLI até que uma política MCP configurável seja justificada. MSBuild ainda não é uma sandbox.

## Alternativas consideradas

- **Chamadas paralelas sem limite:** rejeitadas porque podem multiplicar processos filhos e reduzir o determinismo.
- **Retornar um único relatório em cache durante toda a sessão:** rejeitado porque a invalidação não cobre de forma confiável imports, estado do SDK, estado do Git e mudanças concorrentes no repositório.
- **Registrar paths do repositório e objetos de exceção:** rejeitado porque a correlação operacional não exige contexto sensível.
- **Aplicar o timeout globalmente a Engine e CLI:** rejeitado porque tamanhos de repositório variam e esta decisão trata apenas do host MCP local.

## Referências

- [Servidor MCP](../architecture/mcp-server.md)
- [Performance](../performance.md)
- [ADR 0007](0007-mcp-trust-boundary-hardening.md)
- Issue #131: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/131
