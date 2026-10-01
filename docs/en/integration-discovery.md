# Integration Discovery catalog

**Languages:** English | [Português (Brasil)](../pt-BR/integration-discovery.md)

Integration Discovery is optional, bounded C# syntax analysis. The default inspection remains metadata/MSBuild-only. Enabling it reads eligible source files but never executes target-repository code, performs network discovery, or puts source bodies, queries, payloads, connection strings, credentials, or configuration values in the report.

## Recognized v1.6 catalog

| Kind | Provider/framework | Supported patterns | Directions | Safe resource/target | Limitations | Typical confidence |
| --- | --- | --- | --- | --- | --- | --- |
| `http` | .NET `HttpClient` | `AddHttpClient`, `IHttpClientFactory.CreateClient`, direct client, `BaseAddress`, standard send verbs | `outbound` | named/typed client, safe host, config key | Recognized receivers only; no request body/header capture | high with target, medium otherwise |
| `http` | Refit | `AddRefitClient<T>`, `RestService.For<T>`, `ConfigureHttpClient` | `outbound` | logical API name, contract, safe host/config key | Interface methods are not expanded into a call graph | high/medium |
| `messaging` | RabbitMQ | `BasicPublish`, `BasicConsume` sync/async | `publish`, `consume` | exchange, routing key, queue, config key | No topology/runtime broker discovery | high/medium |
| `messaging` | MassTransit | publish/send endpoints, receive endpoints, consumers | `publish`, `consume` | queue/endpoint, message/consumer contract | Fluent syntax only; no runtime bus topology | high/medium |
| `messaging` | Kafka | `IProducer.Produce*`, `IConsumer.Subscribe` | `publish`, `consume` | topic/config key, value contract | No consumer-group or broker inference | high/medium |
| `messaging` | NServiceBus | send/publish/routing and `IHandleMessages<T>` | `publish`, `consume` | queue and message contract | No runtime routing expansion | high/medium |
| `messaging` | typed custom abstraction | exact `IEventBus`, `IMessageBus`, `IMessagePublisher`, `IMessageConsumer` receivers | `publish`, `consume` | explicit target and generic contract | Technology is `unknown`; arbitrary lookalike APIs are ignored | medium/low |
| `messaging` | AWS SQS | send/batch send/receive on AWS SDK clients | `publish`, `consume` | logical queue/config key | Account-bearing Queue URLs are reduced to queue name | high/medium |
| `messaging` | AWS SNS | publish on AWS SDK clients | `publish` | logical topic/config key | ARN is reduced to logical topic; message is ignored | high/medium |
| `messaging` | AWS EventBridge | `PutEventsAsync` | `publish` | event-bus/config key; presence-only source/detail-type signals | Event detail is never collected | high/medium |
| `messaging` | AWS Kinesis | put/read/shard APIs | `publish`, `consume` | stream/config key | No shard/runtime consumer mapping | high/medium |
| `messaging` | Azure Service Bus | sender send and processor creation | `publish`, `consume` | queue/topic/subscription/config key | Connection strings/SAS are ignored | high/medium |
| `messaging` | Azure Event Hubs | producer send and processor/consumer reads | `publish`, `consume` | event hub/config key | Event data and connection strings are ignored | high/medium |
| `messaging` | Azure Event Grid | publisher send APIs | `publish` | logical topic/domain | Endpoint is reduced to a logical name | high/medium |
| `messaging` | Google Pub/Sub | publisher and subscriber APIs | `publish`, `consume` | topic/subscription/config key | Project identifier is not needed when a logical resource is available | high/medium |
| `database` | PostgreSQL/Npgsql | connection/registration and command execution | `bidirectional`, explicit `read`/`write` | connection-string logical name/config key | SQL and connection-string values are ignored | high/medium |
| `database` | SQL Server/SqlClient | connection/registration and command execution | `bidirectional`, explicit `read`/`write` | connection-string logical name/config key | SQL and credentials are ignored | high/medium |
| `database` | MySQL/MySqlConnector | connection/registration and command execution | `bidirectional`, explicit `read`/`write` | connection-string logical name/config key | SQL and credentials are ignored | high/medium |
| `database` | Oracle Managed Data Access | connection/registration and command execution | `bidirectional`, explicit `read`/`write` | connection-string logical name/config key | SQL and credentials are ignored | high/medium |
| `cache` | Redis | distributed-cache registration/operations and typed Redis receivers | `bidirectional`, explicit `read`/`write` | instance name/config key | Credential-bearing endpoints are ignored | high/medium |
| `database` | MongoDB | client database/collection selection and CRUD APIs | `bidirectional`, explicit `read`/`write` | database/collection/config key | Filters/documents are ignored | high/medium |
| `database` | Azure Cosmos DB | database/container selection and item APIs | `bidirectional`, explicit `read`/`write` | database/container/config key | Documents, partition values, endpoints, and keys are ignored | high/medium |
| `database` | AWS DynamoDB | get/query/scan and write APIs | `read`, `write` | table/config key | Keys/items and account identifiers are ignored | high/medium |
| `database` | Google BigQuery | project/dataset/table clients, reads/inserts/query API | `read`, `write`, `unknown` for arbitrary query text | project/dataset/table/config key | SQL is ignored, so query direction is not invented | high/medium |
| `storage` | Amazon S3 | get/list/put/delete/upload APIs | `read`, `write` | bucket/config key | Object key/body and signed URLs are ignored | high/medium |
| `storage` | Google Cloud Storage | get/list/download/upload/delete APIs | `read`, `write` | bucket/config key | Object name/body and credentials are ignored | high/medium |
| `storage` | Azure Blob Storage | container client and blob download/upload/delete APIs | `bidirectional`, explicit `read`/`write` | container/config key | Blob names/bodies, connection strings, and SAS are ignored | high/medium |

“Supports any messaging” means the detector interface and normalized contract are extensible and the known catalog above is supported. It does not promise recognition of arbitrary application-specific abstractions. A new SDK/provider should be added as a detector plus synthetic fixtures, tests, and this catalog—without provider logic in Engine or Core.

## Bounds and trust boundary

Discovery has limits for visited paths, source files, bytes per file, total bytes, findings, diagnostics, and duration. It skips generated/build-output trees and filesystem reparse points, validates repository-relative provenance, isolates detector failures, and propagates caller cancellation. Syntax parsing reduces risk but is not a general malware sandbox; MSBuild evaluation has its separate trust boundary. Use isolated, ephemeral, least-privileged environments for hostile repositories.
