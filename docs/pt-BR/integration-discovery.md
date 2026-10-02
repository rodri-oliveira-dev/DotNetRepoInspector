# Catálogo do Integration Discovery

**Idiomas:** [English](../en/integration-discovery.md) | Português (Brasil)

Integration Discovery é análise sintática C# opcional e limitada. A inspeção padrão continua somente de metadata/MSBuild. Habilitar a capability lê arquivos de source elegíveis, mas nunca executa código do repositório alvo, faz discovery por rede ou coloca corpos de source, queries, payloads, connection strings, credenciais ou valores de configuração no report.

## Catálogo reconhecido no v1.6

| Kind | Provider/framework | Patterns suportados | Direções | Resource/target seguro | Limitações | Confidence típica |
| --- | --- | --- | --- | --- | --- | --- |
| `http` | .NET `HttpClient` | `AddHttpClient`, `IHttpClientFactory.CreateClient`, client direto, `BaseAddress`, verbos padrão | `outbound` | client named/typed, host seguro, config key | Somente receivers reconhecidos; body/header não é coletado | high com target, medium sem target |
| `http` | Refit | `AddRefitClient<T>`, `RestService.For<T>`, `ConfigureHttpClient` | `outbound` | nome lógico da API, contract, host/config key seguro | Métodos da interface não são expandidos em call graph | high/medium |
| `messaging` | RabbitMQ | `BasicPublish`, `BasicConsume` sync/async | `publish`, `consume` | exchange, routing key, queue, config key | Sem discovery de topologia/broker em runtime | high/medium |
| `messaging` | MassTransit | endpoints de publish/send/receive e consumers | `publish`, `consume` | queue/endpoint, contract da mensagem/consumer | Apenas sintaxe fluent; sem topologia runtime | high/medium |
| `messaging` | Kafka | `IProducer.Produce*`, `IConsumer.Subscribe` | `publish`, `consume` | topic/config key, contract do value | Sem inferência de consumer group ou broker | high/medium |
| `messaging` | NServiceBus | send/publish/routing e `IHandleMessages<T>` | `publish`, `consume` | queue e contract da mensagem | Sem expansão de routing runtime | high/medium |
| `messaging` | abstração customizada tipada | receivers exatos `IEventBus`, `IMessageBus`, `IMessagePublisher`, `IMessageConsumer` | `publish`, `consume` | target explícito e contract genérico | Technology é `unknown`; APIs arbitrárias semelhantes são ignoradas | medium/low |
| `messaging` | AWS SQS | send/batch send/receive em clients do AWS SDK | `publish`, `consume` | queue lógica/config key | Queue URL com account é reduzida ao nome da queue | high/medium |
| `messaging` | AWS SNS | publish em clients do AWS SDK | `publish` | topic lógico/config key | ARN é reduzido ao topic lógico; message é ignorada | high/medium |
| `messaging` | AWS EventBridge | `PutEventsAsync` | `publish` | event bus/config key; signals apenas de presença de source/detail-type | Event detail nunca é coletado | high/medium |
| `messaging` | AWS Kinesis | APIs de put/read/shard | `publish`, `consume` | stream/config key | Sem mapping de shard/consumer runtime | high/medium |
| `messaging` | Azure Service Bus | sender send e criação de processor | `publish`, `consume` | queue/topic/subscription/config key | Connection strings/SAS são ignorados | high/medium |
| `messaging` | Azure Event Hubs | producer send e leituras por processor/consumer | `publish`, `consume` | event hub/config key | Event data e connection strings são ignorados | high/medium |
| `messaging` | Azure Event Grid | APIs de publisher send | `publish` | topic/domain lógico | Endpoint é reduzido a um nome lógico | high/medium |
| `messaging` | Google Pub/Sub | APIs de publisher e subscriber | `publish`, `consume` | topic/subscription/config key | Project identifier não é necessário quando há resource lógico | high/medium |
| `database` | PostgreSQL/Npgsql | connection/registration e execução de command | `bidirectional`, `read`/`write` explícitos | nome lógico da connection string/config key | SQL e valores de connection string são ignorados | high/medium |
| `database` | SQL Server/SqlClient | connection/registration e execução de command | `bidirectional`, `read`/`write` explícitos | nome lógico da connection string/config key | SQL e credenciais são ignorados | high/medium |
| `database` | MySQL/MySqlConnector | connection/registration e execução de command | `bidirectional`, `read`/`write` explícitos | nome lógico da connection string/config key | SQL e credenciais são ignorados | high/medium |
| `database` | Oracle Managed Data Access | connection/registration e execução de command | `bidirectional`, `read`/`write` explícitos | nome lógico da connection string/config key | SQL e credenciais são ignorados | high/medium |
| `cache` | Redis | registration/operações de distributed cache e receivers Redis tipados | `bidirectional`, `read`/`write` explícitos | instance name/config key | Endpoints com credenciais são ignorados | high/medium |
| `database` | MongoDB | seleção de database/collection e APIs CRUD | `bidirectional`, `read`/`write` explícitos | database/collection/config key | Filters/documents são ignorados | high/medium |
| `database` | Azure Cosmos DB | seleção de database/container e APIs de item | `bidirectional`, `read`/`write` explícitos | database/container/config key | Documents, partition values, endpoints e keys são ignorados | high/medium |
| `database` | AWS DynamoDB | APIs get/query/scan e write | `read`, `write` | table/config key | Keys/items e account identifiers são ignorados | high/medium |
| `database` | Google BigQuery | clients de project/dataset/table, reads/inserts/query | `read`, `write`, `unknown` para query arbitrária | project/dataset/table/config key | SQL é ignorado; direction da query não é inventada | high/medium |
| `storage` | Amazon S3 | APIs get/list/put/delete/upload | `read`, `write` | bucket/config key | Object key/body e signed URLs são ignorados | high/medium |
| `storage` | Google Cloud Storage | APIs get/list/download/upload/delete | `read`, `write` | bucket/config key | Object name/body e credenciais são ignorados | high/medium |
| `storage` | Azure Blob Storage | container client e APIs download/upload/delete | `bidirectional`, `read`/`write` explícitos | container/config key | Blob names/bodies, connection strings e SAS são ignorados | high/medium |

“Suportar qualquer mensageria” significa que a interface de detectores e o contrato normalizado são extensíveis e que o catálogo conhecido acima é suportado. Não é garantia de reconhecer abstrações arbitrárias da aplicação. Um novo SDK/provider deve ser adicionado como detector mais fixtures sintéticas, testes e este catálogo—sem lógica de provider na Engine ou no Core.

## Limites e fronteira de confiança

Discovery possui limites de paths visitados, arquivos de source, bytes por arquivo, bytes totais, findings, diagnostics e duração. Ele ignora árvores geradas/de output de build e reparse points, valida proveniência relativa ao repositório, isola falhas de detectores e propaga cancellation. Parsing sintático reduz risco, mas não é sandbox geral contra malware; avaliação MSBuild possui sua fronteira de confiança separada. Use ambientes isolados, efêmeros e com privilégio mínimo para repositórios hostis.
