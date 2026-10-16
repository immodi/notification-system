# Notification Service

## 1. Overview

A small backend service for accepting notifications and processing them asynchronously.

Clients submit a notification through an HTTP API. The service stores the notification, places it on a message queue, and returns immediately. A background worker later consumes the message and delivers the notification through the appropriate notification provider.

The initial implementation supports email notifications using a fake provider. The architecture should allow additional notification channels to be introduced without changing the core processing flow.

### Goals

The project is intended to practice:

* ASP.NET Core Web API
* Clean separation of responsibilities
* Entity Framework Core and SQL DB
* RabbitMQ and asynchronous messaging
* Background processing
* Retry and failure handling
* Dependency injection
* Structured logging
* Testing
* Docker-based local infrastructure

The focus is on understanding how a reliable asynchronous backend system is designed rather than building a large production platform.

---

# 2. Overall Architecture

```text
                   ┌──────────────┐
                   │    Client    │
                   └──────┬───────┘
                          │
                         HTTP
                          │
                          ▼
                 ┌─────────────────┐
                 │   ASP.NET Core  │
                 │       API       │
                 └───────┬─────────┘
                         │
                    create/store
                         │
             ┌───────────┴───────────┐
             │                       │
             ▼                       ▼
      ┌──────────────┐        ┌──────────────┐
      │    SQL DB    │        │   RabbitMQ   │
      └──────────────┘        └──────┬───────┘
                                     │
                                   consume
                                     │
                                     ▼
                           ┌─────────────────┐
                           │ Background      │
                           │ Processing      │
                           └────────┬────────┘
                                    │
                                    ▼
                           ┌─────────────────┐
                           │  Notification   │
                           │    Provider     │
                           └────────┬────────┘
                                    │
                                    ▼
                                   Email
```

The system is divided into four main concerns:

**API**
Responsible for accepting requests, validating input, and exposing notification status.

**Persistence**
Responsible for storing notification state and processing information.

**Messaging**
Responsible for decoupling request handling from notification delivery through RabbitMQ.

**Background processing**
Responsible for consuming queued notifications, attempting delivery, and handling failures.

---

# 3. Notification Lifecycle

A notification moves through a small set of states:

```text
Queued → Processing → Sent
                     │
                     └── Failed → Retry → Processing
                                      │
                                      └── exhausted → Dead Letter Queue
```

A newly created notification starts in the `Queued` state.

Once a worker begins processing it, the state becomes `Processing`.

A successful delivery changes the state to `Sent`.

A failed delivery is recorded and retried when appropriate. Notifications that continue to fail after the configured retry limit are considered permanently failed and are moved to the dead-letter queue.

The database represents the current state of the notification throughout this lifecycle.

---

# 4. Request and Processing Flow

The main API operation is creating a notification.

```text
Client
  │
  ▼
HTTP request
  │
  ▼
API
  │
  ├── Validate request
  ├── Create notification
  ├── Store notification
  └── Publish message
          │
          ▼
       RabbitMQ
          │
          ▼
   Background worker
          │
          ▼
 Notification provider
```

The API does not perform the actual delivery.

Instead, it accepts the request and queues the work. This allows the HTTP request to remain independent of the delivery provider and avoids making the client wait for potentially slow or unreliable external operations.

The creation endpoint therefore returns `202 Accepted` once the notification has successfully entered the asynchronous processing pipeline.

---

# 5. Messaging Architecture

RabbitMQ acts as the boundary between accepting a notification and processing it.

The message should contain only the information required to identify the work, primarily the notification identifier.

The worker uses that identifier to retrieve the current notification state from the database before processing it.

This keeps the database as the source of truth while RabbitMQ represents pending work.

The messaging layer should support:

* Normal notification delivery
* Explicit acknowledgements
* Retry handling
* Dead-lettering
* At-least-once delivery

The system assumes that a message may occasionally be delivered more than once. Consumers therefore need to tolerate duplicate delivery safely.

---

# 6. Background Processing

Notification delivery is performed by an ASP.NET Core hosted background worker.

The worker continuously consumes messages from RabbitMQ and processes them independently from incoming HTTP requests.

For each message, the worker:

1. Retrieves the notification.
2. Verifies that it still needs processing.
3. Marks it as processing.
4. Attempts delivery through the notification provider.
5. Updates its state based on the result.
6. Acknowledges the message after successful processing.

This separation keeps the API lightweight while allowing the processing side of the system to scale independently.

---

# 7. Notification Providers

Notification delivery is isolated behind a provider boundary.

The processing system should not contain email-specific implementation details.

The initial provider is a fake email provider that simulates successful or failed delivery. This keeps the project focused on the architecture rather than integrating with a real email service.

The same processing flow should later support additional channels such as SMS or push notifications without redesigning the rest of the system.

---

# 8. Reliability and Failure Handling

The main reliability concern is that notification delivery depends on components that may fail independently.

Examples include:

* RabbitMQ becoming unavailable
* The worker crashing
* The notification provider failing
* Temporary network failures
* A notification being processed more than once

The system therefore uses retry handling for transient failures.

A typical retry policy uses exponential backoff:

```text
1st retry → 1 second
2nd retry → 2 seconds
3rd retry → 4 seconds
```

After the retry limit is reached, the notification is treated as permanently failed and moved to the dead-letter queue.

RabbitMQ messages are acknowledged only after successful processing so that a worker failure does not silently discard work.

---

# 9. Duplicate Processing and Idempotency

The service uses the notification identifier as the stable identity of a notification.

Before processing, the worker checks the current notification state. If the notification has already been successfully processed, it should not be delivered again.

This provides basic idempotency and protects against duplicate message delivery.

There is still a small failure window between successfully sending a notification and acknowledging the RabbitMQ message. A production system would need stronger idempotency guarantees at the provider or messaging boundary.

The project intentionally keeps this limitation visible rather than hiding it behind unnecessary complexity.

---

# 10. Persistence

The SQL DB stores the notification and its processing state.

The notification record contains the information needed to:

* Identify the notification
* Deliver it
* Track its lifecycle
* Count retries
* Record failures
* Determine when processing completed

The database is the source of truth for notification state, while RabbitMQ is responsible for transporting work between the API and background processing.

Indexes should support common operations such as retrieving notifications by status and time.

---

# 11. Error Handling and Observability

The API should expose consistent error responses using ASP.NET Core problem details.

Unexpected exceptions should be handled centrally rather than implemented independently in every endpoint.

The application should also use structured logging around major lifecycle events, such as:

```text
Notification queued
Notification processing
Notification delivered
Notification failed
Notification retrying
Notification moved to dead-letter queue
```

Logs should provide enough context to trace a notification through the system without unnecessarily recording sensitive notification content.

---

# 12. Configuration and Deployment

Infrastructure-specific settings such as database and RabbitMQ connection details should come from application configuration rather than being hardcoded.

The application should be runnable locally with Docker-based dependencies:

```text
ASP.NET Core
SQL DB
RabbitMQ
```

Docker Compose provides a simple environment in which the complete system can be run and tested together.

---

# 13. Testing Strategy

Testing should focus on the important system behavior rather than implementation details.

The main scenarios are:

```text
Notification is accepted and stored
Notification is published for processing
Notification is successfully delivered
Notification failure triggers a retry
Retry exhaustion results in dead-lettering
Already-sent notifications are not delivered again
Notification status can be retrieved
```

Both isolated application tests and integration tests should be used where they provide value.

---

# 14. Future Improvements

Once the core system is working, it could be extended with:

**Outbox pattern**
Guarantees consistency between database changes and message publishing.

**Multiple notification channels**
Email, SMS, push notifications, and other providers.

**Scheduled notifications**
Allow notifications to be delivered at a future time.

**Priority handling**
Process urgent notifications ahead of normal work.

**Metrics and monitoring**
Track delivery rates, retry counts, failures, and processing latency.

**Authentication and authorization**
Control which clients can submit and inspect notifications.

---

# 15. Definition of Done

The core system is complete when the following flow works reliably:

```text
HTTP request
     │
     ▼
ASP.NET Core API
     │
     ├── Store notification
     │
     └── Publish message
              │
              ▼
           RabbitMQ
              │
              ▼
       Background worker
              │
              ▼
      Notification provider
              │
              ▼
        Update database
```

A successful notification should progress through:

```text
Queued → Processing → Sent
```

A failed notification should demonstrate:

```text
Queued → Processing → Failed
                    ↓
                   Retry
                    ↓
              Failed / DLQ
```

The main purpose of the project is to build and understand this complete asynchronous processing flow and be able to explain the reasoning behind each architectural decision.
