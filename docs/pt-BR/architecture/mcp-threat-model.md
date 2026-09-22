# Threat model do servidor MCP

**Idiomas:** [English](../../en/architecture/mcp-threat-model.md) | Português (Brasil)

Este threat model cobre o servidor local stdio `DotNetRepoInspector.Mcp` v1. Ele complementa o [modelo de segurança](../security.md) do repositório, a [ADR 0006](../decisions/0006-mcp-adapter-architecture.md) e a [ADR 0007](../decisions/0007-mcp-trust-boundary-hardening.md).

## Specification

### Ativos protegidos

- credenciais e variáveis de ambiente mantidas pelo processo do cliente MCP;
- arquivos fora do repository root selecionado explicitamente;
- integridade do protocolo em stdin/stdout e logs operacionais em stderr;
- disponibilidade do host MCP e do cliente;
- integridade do repositório inspecionado, que não deve ser alterado por tools v1;
- o contrato `InspectionReport` determinístico e sem dados sensíveis.

### Limites de confiança

1. **Cliente MCP para servidor:** nomes de tools e argumentos JSON não são confiáveis.
2. **Servidor para filesystem do repositório:** `--root` é o limite lógico de filesystem para paths controlados por tools.
3. **Servidor para Engine e processos filhos:** Git e MSBuild operam com a identidade do SO e o ambiente restante do servidor.
4. **Repositório para MSBuild:** project files, imports, SDK resolvers, condições e property functions são controlados pelo repositório e não são confiáveis.
5. **Servidor para cliente:** resultados estruturados, erros de protocolo, logs e diagnósticos não devem revelar secrets operacionais.

### Requisitos de segurança

- exigir um `--root` existente, canonicalizar links no caminho do root uma vez no startup e manter o resultado imutável;
- aceitar somente paths normalizados e relativos ao repositório, rejeitando escapes lexicais, paths absolutos e qualquer symlink/junction abaixo do root canônico;
- manter todas as tools v1 read-only, closed-world, idempotentes e sem inputs de comandos arbitrários;
- remover variáveis de ambiente com aparência de credencial antes de hospedar ou iniciar processos filhos do Engine;
- aplicar normalização/redaction canônica do report antes de outputs completos ou granulares;
- limitar comprimentos de paths, contagens de coleções, tamanho de configuração e tamanho de resultados MCP;
- usar erros estáveis e sanitizados e reservar stdout ao MCP;
- propagar cancelamento e encerrar árvores de processos filhos;
- declarar claramente que esses controles não transformam MSBuild em sandbox.

## Ameaças E Controles

| Ameaça | Controles técnicos | Risco residual |
| --- | --- | --- |
| Path traversal ou acesso por path absoluto | `Path.GetFullPath`, `Path.GetRelativePath`, verificação de contenção no root, schemas fechados e rejeição estável antes do Engine. | Arquivos podem mudar após a validação (TOCTOU). |
| Escape por symlink/junction | Links do caminho do root são resolvidos no startup; paths de tools que cruzam qualquer `ReparsePoint` abaixo do root são rejeitados; discovery ignora reparse points. | Um link pode ser trocado após a validação. Imports e property functions do MSBuild não são limitados por essa fronteira lógica. |
| Injeção de comandos | Contratos expõem somente opções tipadas; processos filhos usam `ArgumentList`, sem shell; cliente não escolhe target ou executável. | Avaliação MSBuild controlada pelo repositório pode usar capacidades do próprio MSBuild. |
| Herança de secrets | O processo MCP remove nomes semelhantes a credenciais e variáveis que apontam para handles/configurações; processos `dotnet` reaplicam o filtro e desabilitam telemetria/node reuse. | Filtro por nome não é DLP; secrets com nomes incomuns, arquivos legíveis, credential stores e identidades de rede podem continuar acessíveis. |
| Exposição de secrets | Erros esperados são estáticos e sanitizados; exception/output bruto não é retornado; serialização canônica redige chaves sensíveis de contexto; credenciais em remotes Git são sanitizadas. | Metadata do repositório prevista no contrato permanece visível ao cliente. |
| Exaustão de recursos | Paths relativos de 1.024 caracteres, 256 exclusões, 256 overrides, valores de override de 128 caracteres, configuração de 1 MiB e resultados MCP de 8 MiB. Cancelamento é suportado. | A inspeção ainda avalia projetos descobertos antes do limite de output. Limites de tempo/performance pertencem ao hardening de confiabilidade. |
| Mutação do repositório | Tools anunciam read-only/non-destructive e chamam apenas APIs de inspeção. Não há tool de escrita, shell, restore, build, persistência ou upload. | Avaliação MSBuild não é garantidamente livre de efeitos colaterais diante de lógica hostil. Isolamento do SO é necessário para repositórios não confiáveis. |
| Injeção em protocolo/logs | stdout pertence ao transporte stdio; logs usam stderr; stdin de filhos é fechado; valores de argumentos e exceptions brutas não são logados. | Uma dependência ou runtime comprometido pode violar premissas no nível do processo. |

## Política De Links E TOCTOU

Um root informado explicitamente pode ser um symbolic link ou junction. O servidor resolve cada segmento linkado existente no caminho do root e armazena o diretório absoluto final como âncora de confiança. Abaixo dessa âncora, paths controlados por tools não podem cruzar symbolic links, junctions ou outros reparse points, mesmo quando o destino permanece dentro do repositório. Essa regra conservadora é consistente entre plataformas; testes condicionais são ignorados somente quando o SO não permite criar links.

A validação é uma verificação, não uma transação de filesystem. Outro processo com permissão de escrita pode substituir um componente validado antes do acesso pelo Engine. Eliminar essa corrida exige travessia relativa a handles e específica do SO ou sandbox, o que não é fornecido. Execute o servidor sobre um repositório que atores não confiáveis não possam alterar concorrentemente.

MSBuild pode resolver imports, SDKs, project references, property functions e ferramentas externas fora do root lógico. Esses paths não são argumentos de tools do cliente e não podem ser confinados de modo confiável por validação lexical. Trate os repositórios como confiáveis ou inspecione-os em ambiente efêmero, non-privileged, sem secrets e com acesso restrito a filesystem/rede.

## Plan E Tasks

1. Canonicalizar o root de startup e rejeitar links abaixo dele para todo input de tool que contenha paths, inclusive a configuração padrão.
2. Aplicar limites a inputs, arquivos de configuração e resultados antes da resposta MCP.
3. Remover nomes sensíveis do ambiente no startup MCP e manter o hardening existente dos processos `dotnet`.
4. Canonicalizar/redigir reports antes de projeções granulares e comprovar que falhas esperadas não expõem detalhes de exceptions.
5. Comprovar annotations read-only, rejeição de traversal, política de links, limites, redaction, cancelamento, isolamento de stdout e ausência de regressões em CLI/Engine.

## Matriz De Validação

Testes automatizados cobrem root obrigatório/ausente/linkado, paths válidos normalizados, traversal e paths absolutos, arquivos de configuração explícito/padrão linkados, diretórios linkados, coleções/configurações/resultados grandes demais, classificação de nomes sensíveis de ambiente, redaction de diagnósticos, schemas fechados, annotations read-only, cancelamento do protocolo, separação stderr/stdout e shutdown limpo. Testes de links executam quando o SO permite criar symbolic links.

## Não Objetivos

Este hardening não fornece sandbox de filesystem, rede ou processos, fronteira contra malware, camada de autorização nem garantia de que avaliar um grafo MSBuild hostil seja livre de efeitos colaterais. Transporte HTTP e tools de escrita continuam fora de escopo.
