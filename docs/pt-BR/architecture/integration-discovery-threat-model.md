# Threat model do Integration Discovery

**Idiomas:** [English](../../en/architecture/integration-discovery-threat-model.md) | Português (Brasil)

Este modelo cobre a fronteira opt-in de análise sintática C# descrita pela [ADR 0018](../decisions/0018-integration-discovery-boundary.md). O repositório inspecionado, cada caminho elegível e cada byte de source não são confiáveis. Os ativos a proteger são disponibilidade e memória do host, integridade da fronteira do repositório, output determinístico e confidencialidade de source, credenciais, queries e payloads.

| Ameaça | Controle | Risco residual |
| --- | --- | --- |
| Arquivos enormes ou muitos arquivos | Budgets independentes de paths, arquivos, bytes, findings, diagnósticos e duração; checagem de tamanho antes da leitura | Trabalho até os budgets configurados é intencional |
| Syntax trees patológicas | Parsing somente sintático, budget de duração, cancellation e isolamento por detector | Roslyn ainda consome CPU/memória dentro dos limites do processo |
| Strings maliciosas ou com formato de segredo | Projeção de evidências em allowlist, normalização de recursos seguros e ausência de snippets ou serialização de literais arbitrários | Um identificador lógico escolhido pelo autor pode ser sensível; operadores devem manter identificadores sem segredos |
| Traversal, reparse points e symlinks | Caminhos canônicos relativos ao repositório e policy existente de rejeição de links/reparse points | Resistência a race conditions depende do host e deve ser reforçada com isolamento do SO para repositórios hostis |
| Código gerado e output de build | Marcadores de arquivo gerado e exclusões de diretórios de build | Código gerado com nome não convencional pode ser elegível até outro budget ou exclusão atuar |
| Cancellation ou falha de detector | Cancellation é propagado; falhas de detector viram diagnósticos controlados e limitados sem expor texto de exceção | Encerramento abrupto do host pode impedir a produção do report |
| Esgotamento do budget de findings | Travessia determinística e limite global de findings com metadata explícita de truncation | Findings depois do cutoff determinístico são intencionalmente omitidos |

Integration Discovery não executa, compila nem carrega código alvo e não faz discovery por rede. Ele não é um sandbox de sistema operacional. A avaliação de projetos pelo MSBuild possui uma fronteira de confiança separada e mais ampla; repositórios hostis ainda devem ser inspecionados com identidade descartável, de menor privilégio e sem rede, conforme o [guia de segurança](../security.md).
