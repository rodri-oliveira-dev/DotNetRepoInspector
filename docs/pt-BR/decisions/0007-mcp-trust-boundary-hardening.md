# ADR 0007: Endurecer a fronteira de confiança do MCP local

**Idiomas:** [English](../../en/decisions/0007-mcp-trust-boundary-hardening.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-09-19
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

A ADR 0006 definiu um adapter MCP local, stdio e read-only com repository root explícito. A validação de paths das tools era lexical, enquanto a configuração do repositório ainda podia ser acessada por um link abaixo do root. O processo MCP também herdava todo o ambiente do cliente antes da aplicação do filtro existente aos processos filhos `dotnet`. Inputs grandes do cliente e resultados de inspeção não possuíam limite no adapter.

A avaliação MSBuild continua capaz de ler variáveis de ambiente, arquivos, imports e recursos de rede disponíveis à identidade do SO. Verificações de paths não transformam essa avaliação em sandbox.

## Decisão

A fronteira de confiança do MCP v1 usa as seguintes regras de defesa em profundidade:

1. Resolver segmentos linkados no caminho do root explícito uma vez no startup e armazenar o diretório absoluto final.
2. Rejeitar todo path controlado por tools que seja absoluto, escape lexicalmente ou cruze symbolic link, junction ou reparse point abaixo desse root. A descoberta de projetos continua ignorando reparse points.
3. Aplicar a mesma regra a arquivos de configuração explícitos e padrão. Limitar arquivos de configuração a 1 MiB.
4. Limitar paths relativos a 1.024 caracteres, exclusões e overrides a 256 entradas cada, valores de override a 128 caracteres e resultados MCP a 8 MiB UTF-8.
5. Remover do processo MCP variáveis de ambiente com aparência de credencial e variáveis que apontam para handles/configurações de credenciais antes do startup do host/Engine. Manter o segundo filtro existente nos processos filhos `dotnet`/MSBuild.
6. Serializar e desserializar canonicamente um `InspectionReport` antes das projeções MCP, reutilizando normalização e redaction de contextos sensíveis de diagnósticos.
7. Manter toda tool v1 read-only, não destrutiva, idempotente, closed-world e sem inputs de comandos/executáveis.

As verificações falham de forma fechada com erros estáveis (`path_through_link`, `input_too_large` ou `result_too_large`) e não repetem os valores recebidos.

## Consequências

O adapter impede traversal direto por links escolhidos pelo cliente e limita payloads comuns de negação de serviço acidental sem alterar `InspectionReport`. Repositórios que colocam intencionalmente a configuração do DotNetRepoInspector atrás de um link devem usar um arquivo regular dentro do root.

A filtragem do ambiente pode impedir resolução de SDK/feed privado que dependa de variáveis de credenciais. Pré-provisionar dependências ou usar uma identidade isolada dedicada é preferível a expor credenciais à avaliação não confiável.

TOCTOU continua possível porque validação e uso são operações separadas. MSBuild pode seguir imports/references controlados pelo repositório ou executar property functions fora do root lógico. Repositórios não confiáveis devem ser inspecionados somente dentro de uma fronteira externa de SO/container/VM sem secrets e com acesso restrito a filesystem/rede.

## Alternativas consideradas

- **Permitir links cujo destino atual esteja dentro do root:** rejeitada porque a troca do destino cria uma superfície de corrida maior e mais difícil de explicar.
- **Limpar todo o ambiente do processo:** rejeitada porque descoberta do SDK e startup de processos exigem um ambiente mínimo da plataforma; remover por nome preserva compatibilidade enquanto reduz exposição.
- **Retornar reports arbitrariamente grandes:** rejeitada porque clientes e hosts stdio precisam de um teto previsível para respostas.
- **Afirmar que validação do root isola MSBuild:** rejeitada por ser tecnicamente falsa.

## Referências

- [Threat model MCP](../architecture/mcp-threat-model.md)
- [Segurança e privacidade](../security.md)
- [ADR 0006](0006-mcp-adapter-architecture.md)
- Issue #130: https://github.com/rodri-oliveira-dev/DotNetRepoInspector/issues/130
