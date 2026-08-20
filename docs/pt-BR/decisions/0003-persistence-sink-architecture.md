# ADR 0003: Manter persistência de snapshots opcional atrás de adapters de sink

**Idiomas:** [English](../../en/decisions/0003-persistence-sink-architecture.md) | Português (Brasil)

- **Status:** Aceito
- **Data:** 2026-08-20
- **Responsáveis pela decisão:** mantenedores do DotNetRepoInspector

## Contexto

O DotNetRepoInspector produz um `InspectionReport` normalizado que é útil tanto como saída imediata de CI/CD quanto como evidência histórica. Consumidores podem querer reter snapshots para analisar versões do .NET, tipos de aplicação, dependências ou mudanças arquiteturais ao longo do tempo.

O fluxo de inspeção precisa continuar útil sem infraestrutura externa. `Core` não deve conhecer bancos de dados, HTTP, cloud providers, plataformas de CI, credenciais, retries ou políticas de retenção.

O design de persistência também precisa considerar:

- uso pela CLI, GitHub Actions e outros hosts;
- destinos implementados por terceiros;
- evolução do schema;
- timeout e cancelamento;
- falha de persistência configurável como fatal ou não fatal;
- fronteiras de retry e idempotência;
- proteção de credenciais e outros secrets.

## Decisão

Persistência será uma integração opcional posterior à inspeção, implementada atrás de uma abstração de sink fora do `Core` e fora da `Engine` de inspeção.

Um novo assembly `DotNetRepoInspector.Persistence` contém o contrato de extensão independente de provider e a política de publicação. Ele depende somente de `DotNetRepoInspector.Core`.

O fluxo normal é:

```text
repositório
   |
   v
Engine -> InspectionReport
              |
              | opcional e explícito
              v
      InspectionSnapshotPublisher
              |
              v
       IInspectionSnapshotSink
              |
              v
        destino externo
```

A `Engine` não descobre, configura nem invoca sinks. Uma camada de delivery só chama persistência depois que já possui um `InspectionReport`.

### Contrato de extensão

Destinos de terceiros implementam `IInspectionSnapshotSink` e recebem um `InspectionSnapshot` mais um `CancellationToken`.

O envelope inicial `InspectionSnapshot` contém o relatório normalizado. A issue #21 ampliará esse envelope de evidência com metadados estáveis de identidade/proveniência antes do primeiro sink de rede. O envelope já existe agora para permitir evolução da API de sink sem acoplar adapters de destino à engine de inspeção.

Um sink retorna `InspectionSinkWriteResult` para resultados operacionais esperados. Falhas contêm:

- código estável pertencente ao adapter;
- mensagem segura e legível;
- classificação `IsTransient` quando o adapter puder determiná-la.

Mensagens de sink não podem conter credenciais, headers de autorização, connection strings, dumps brutos de exceção ou corpos de resposta contendo secrets.

Exceções inesperadas que atravessem a fronteira de extensão são normalizadas por `InspectionSnapshotPublisher` para o resultado genérico `unexpected-sink-failure`, sem copiar o texto da exceção.

### Opt-in e semântica de falha

Persistência é desabilitada por ausência: se um host não selecionar/configurar explicitamente um sink, nenhum objeto de persistência é criado e nenhuma chamada externa é feita.

`InspectionPersistenceOptions` define política de execução independente de provider:

- timeout padrão: 15 segundos;
- modo de falha padrão: `NonFatal`;
- modo opcional `Fatal` para pipelines em que persistir a evidência é obrigatório.

Uma falha de persistência não modifica `InspectionReport` e não cria diagnóstico de inspeção `DRI`. `InspectionPersistenceResult.ShouldFailExecution` informa à camada de delivery se essa falha separada deve fazer o comando/job falhar.

Cancelamento do chamador é propagado. Timeout do publisher é representado por `persistence-timeout` e classificado como transitório.

### Responsabilidade de retry

O publisher genérico **não** faz retry.

Retry pertence ao sink concreto porque somente esse adapter pode saber de forma confiável:

- quais erros do destino são transitórios;
- se uma requisição pode ter sido aceita antes de uma falha de transporte;
- quais semânticas de backoff/retry o destino suporta;
- se replay é seguro segundo o contrato de idempotência.

Um sink concreto poderá repetir apenas falhas transitórias, com tentativas/backoff limitados, respeitando timeout total e cancelamento. A issue #21 define identidade e idempotência antes da issue #22 implementar um sink de rede.

### Configuração e credenciais

Configuração de persistência pertence à camada de delivery e é deliberadamente separada do `.dotnetrepoinspector.json`, que configura inspeção/classificação do repositório.

Um sink concreto poderá expor configurações não sensíveis por opções da CLI/Action, por exemplo seleção do sink, identificador de endpoint, timeout ou modo de falha. Secrets devem ser fornecidos por mecanismos apropriados ao host, como variáveis de ambiente ou GitHub Actions secrets.

Credenciais nunca se tornam campos de `InspectionReport` ou `InspectionSnapshot` e não podem ser copiadas para contexto de diagnóstico ou logs normais.

### Primeiro sink built-in

O primeiro sink built-in concreto será um adapter HTTP/webhook, implementado pela issue #22 depois da issue #21.

HTTP/webhook foi escolhido porque:

- funciona em automação local e sistemas comuns de CI/CD;
- delega a tecnologia de armazenamento para um serviço controlado pelo consumidor;
- evita acoplar o Inspector a banco de dados ou cloud provider;
- é simples para terceiros imitarem ou substituírem;
- consegue transportar o payload canônico e versionado de evidência.

O sink HTTP não ficará no `Core` nem na `Engine`. Será um adapter/package separado sobre a abstração de persistência.

## Alternativas consideradas

### Persistência direta em banco configurável

**Rejeitada como arquitetura principal.**

Vantagens:

- escrita direta pode ser conveniente em um ambiente interno conhecido;
- primitivas nativas de upsert/idempotência podem existir no banco.

Trade-offs:

- acopla o Inspector a drivers, dialetos SQL, migrations, pooling, connection strings e semânticas específicas de falha;
- aumenta a superfície de dependências e vulnerabilidades;
- é pouco conveniente para consumidores com bancos ou storages diferentes;
- torna tratamento de secrets mais complexo em uma CLI/Action genérica.

Um sink de terceiros ainda poderá implementar persistência direta em banco.

### Arquivo/objeto como único mecanismo de persistência

**Mantido como interoperabilidade, rejeitado como única arquitetura de persistência.**

A saída JSON existente já é um artifact útil e pode ser enviada pelo CI para object storage. É a opção de menor acoplamento e continua suportada.

Porém, apenas saída em arquivo não oferece uma fronteira uniforme de extensão para serviços remotos de evidência, bancos de aplicações ou sistemas personalizados de retenção. Portanto, complementa em vez de substituir sinks.

### Apenas HTTP/webhook built-in, sem interface

**Rejeitado.**

Seria simples inicialmente, mas tornaria semântica HTTP parte da fronteira da aplicação e forçaria destinos futuros para dentro da CLI/Engine ou para código duplicado de integração.

### Sistema de descoberta/carregamento dinâmico de plugins

**Rejeitado para a versão inicial.**

Descoberta de plugins em runtime introduziria complexidade de assembly loading, confiança, compatibilidade de versões, packaging e segurança sem ser necessária para tornar o contrato extensível.

Terceiros podem referenciar `DotNetRepoInspector.Persistence` e implementar `IInspectionSnapshotSink` em seu próprio host/package. Um loader dinâmico poderá ser considerado mais tarde se demanda real justificar a complexidade.

## Consequências

### Positivas

- inspeção permanece zero-infrastructure e determinística;
- `Core` continua agnóstico de provider;
- `Engine` continua responsável apenas pela inspeção;
- falhas de persistência não alteram fatos de inspeção;
- camadas de delivery podem escolher comportamento fatal ou não fatal;
- timeout e cancelamento têm contrato comum e independente de provider;
- terceiros possuem uma interface pequena sem tipos específicos do GitHub;
- HTTP pode ser implementado sem fixar o projeto a um backend de armazenamento.

### Trade-offs

- hosts de delivery precisam compor persistência explicitamente após a inspeção;
- não existe retry genérico porque segurança de replay é específica do destino;
- proveniência/idempotência depende do contrato seguinte da issue #21;
- o primeiro sink concreto continua indisponível até a issue #22;
- descoberta dinâmica de plugins não é fornecida de propósito.

## Compatibilidade e versionamento

A evidência persistida deve preservar o `schemaVersion` da inspeção. Consumidores devem aplicar as regras existentes de compatibilidade do schema ao ler payloads históricos.

Configuração de transporte e falhas de sink não fazem parte do schema de inspeção. Adicionar ou alterar um sink não exige bump do schema, a menos que o próprio payload normalizado de evidência seja alterado.

O envelope `InspectionSnapshot` é o ponto de extensão para os metadados de proveniência definidos pela issue #21.

## Segurança

Esta decisão segue o modelo de segurança do projeto:

- persistência é opt-in;
- nenhuma credencial de sink pertence ao JSON de inspeção;
- secrets específicos de sink vêm de mecanismos externos de secrets;
- mensagens de falha são resumos seguros, não payloads/exceções brutos do destino;
- exceções inesperadas de sinks são normalizadas sem texto de exceção;
- avaliação de repositório não confiável e credenciais de persistência não devem compartilhar ambiente privilegiado sem revisão explícita desse trust boundary.

Consulte [`../security.md`](../security.md).

## Próximos trabalhos

- **#21** — definir identidade da evidência, proveniência, timestamp UTC, metadados de CI e semântica da chave de idempotência.
- **#22** — implementar o primeiro sink HTTP/webhook com retry transitório limitado, timeout/cancelamento, configuração segura de secrets e suporte a idempotência.

## Referências

- Contrato de inspeção: [`../schema/inspection-v1.md`](../schema/inspection-v1.md)
- Contrato de persistência: [`../persistence.md`](../persistence.md)
- Modelo de segurança: [`../security.md`](../security.md)
