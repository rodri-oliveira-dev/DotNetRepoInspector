# Política de Segurança

**Idiomas:** [English](SECURITY.md) | Português (Brasil)

## Relatando uma vulnerabilidade

Por favor, não abra uma issue pública para uma vulnerabilidade suspeita que possa expor segredos, dados de repositórios privados, permitir execução arbitrária de código ou comprometer o ambiente de outro usuário.

Dê preferência ao recurso de relato privado de vulnerabilidades do GitHub / Security Advisories deste repositório quando a opção **Report a vulnerability** estiver disponível na aba **Security** do repositório. Inclua:

- a versão ou o commit afetado do DotNetRepoInspector;
- o modo de execução (CLI, .NET Tool ou GitHub Action);
- os passos para reprodução ou um repositório mínimo, quando for seguro compartilhá-lo de forma privada;
- o impacto de segurança e quaisquer pré-condições conhecidas;
- se credenciais ou dados privados podem já ter sido expostos.

Se o relato privado de vulnerabilidades não estiver disponível, entre em contato com o mantenedor pelos meios de contato disponíveis em seu perfil do GitHub e solicite um canal privado antes de compartilhar detalhes de exploração ou segredos.

O mantenedor confirmará o recebimento de um relato completo assim que for viável, reproduzirá e avaliará o problema, coordenará uma correção e publicará as informações de remediação depois que os usuários afetados tiverem uma oportunidade razoável de atualizar suas instalações.

## Versões suportadas

Antes do primeiro lançamento público estável, as correções de segurança têm como alvo a branch atual `main`. Depois que os lançamentos estáveis começarem, o projeto documentará aqui as linhas de versões suportadas e priorizará correções para as versões major/minor suportadas mais recentes.

## Modelo de segurança

O DotNetRepoInspector inspeciona metadados MSBuild avaliados. **A avaliação do MSBuild não é um sandbox.** Inspecione repositórios não confiáveis somente em um ambiente isolado, efêmero e sem privilégios, que não contenha credenciais nem dados aos quais o repositório inspecionado não deva ter acesso.

O escopo detalhado de coleta, o modelo de confiança do MSBuild, as permissões da GitHub Action, as medidas de hardening do ambiente, as regras de logging e as orientações sobre credenciais dos sinks estão documentados em [`docs/en/security.md`](docs/en/security.md) e [`docs/pt-BR/security.md`](docs/pt-BR/security.md).
