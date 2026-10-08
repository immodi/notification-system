# Notification Service

## 1. Overview

A small ASP.NET Core backend service for accepting notifications and processing them asynchronously through RabbitMQ.

The API accepts a notification request, stores it in the database, publishes a message to RabbitMQ, and returns immediately. A background worker consumes messages from RabbitMQ and delivers the notification through a provider.

The initial version will support **email notifications**, but the design should make it easy to add SMS or other notification channels later.

### Main goal

Practice real backend concepts:

* ASP.NET Core Web API
* Dependency Injection
* EF Core
* RabbitMQ
* Background workers
* Asynchronous processing
* Retry handling
* Dead-letter queues
* Structured logging
* Error handling
* Unit and integration testing

---

# 2. Scope

## MVP

The service should support:

1. Create a notification
2. Store the notification
3. Publish it to RabbitMQ
4. Consume it asynchronously
5. Send the notification
6. Track its status
7. Retry failed deliveries
8. Move permanently failed notifications to a dead-letter queue
9. Retrieve notification status

### API

```text
POST /api/notifications
GET  /api/notifications/{id}
```

Example request:

```json
{
  "recipient": "user@example.com",
  "subject": "Welcome",
  "message": "Welcome to our application!"
}
```

Example response:

```json
{
  "id": "7f4f6e5e-2f4d-4e2d-a3d1-8a8d7c1a1234",
  "status": "Queued"
}
```

---

# 3. High-Level Architecture

```text
                    ┌─────────────────┐
                    │     Client      │
                    └────────┬────────┘
                             │
                             │ HTTP
                             ▼
                    ┌─────────────────┐
                    │  ASP.NET Core   │
                    │      API        │
                    └───────┬─────────┘
                            │
                    ┌───────┴────────┐
                    │                │
                    ▼                ▼
              ┌──────────┐    ┌─────────────┐
              │ SQL DB   │    │  RabbitMQ   │
              └──────────┘    └──────┬──────┘
                                     │
                                     │ consume
                                     ▼
                            ┌─────────────────┐
                            │ Background      │
                            │ Worker          │
                            └────────┬────────┘
                                     │
                                     ▼
                            ┌─────────────────┐
                            │ Notification    │
                            │ Provider        │
                            └─────────────────┘
                                     │
                                     ▼
                                  Email
```

---

# 4. Project Structure

Keep the architecture simple.

```text
NotificationService/
│
├── NotificationService.Api/
│   ├── Controllers/
│   │   └── NotificationsController.cs
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs
│   └── Program.cs
│
├── NotificationService.Application/
│   ├── DTOs/
│   │   ├── CreateNotificationRequest.cs
│   │   └── NotificationResponse.cs
│   ├── Interfaces/
│   │   ├── INotificationService.cs
│   │   ├── IMessagePublisher.cs
│   │   └── INotificationProvider.cs
│   └── Services/
│       └── NotificationService.cs
│
├── NotificationService.Domain/
│   ├── Entities/
│   │   └── Notification.cs
│   └── Enums/
│       └── NotificationStatus.cs
│
├── NotificationService.Infrastructure/
│   ├── Data/
│   │   └── AppDbContext.cs
│   ├── Messaging/
│   │   ├── RabbitMqPublisher.cs
│   │   └── RabbitMqConsumer.cs
│   └── Providers/
│       └── EmailNotificationProvider.cs
│
└── NotificationService.Tests/
```

Don't create a separate project for every tiny abstraction. The purpose is to practice architecture, not architecture astronautics.

---

# 5. Domain Model

## Notification

```text
Notification
-------------------------
Id
Recipient
Subject
Message
Status
RetryCount
CreatedAt
ProcessedAt
LastError
```

### Status

```text
Queued
Processing
Sent
Failed
```

A notification starts as:

```text
Queued
```

Then:

```text
Queued
   ↓
Processing
   ↓
Sent
```

If delivery fails:

```text
Processing
   ↓
Failed
   ↓
retry
   ↓
Processing
```

After the maximum number of retries:

```text
Failed
   ↓
Dead Letter Queue
```

---

# 6. API Flow

## POST /api/notifications

### Request

```json
{
  "recipient": "user@example.com",
  "subject": "Order Confirmed",
  "message": "Your order has been confirmed."
}
```

### Flow

```text
Client
  │
  ▼
Controller
  │
  ▼
Application Service
  │
  ├── Create Notification
  │
  ├── Save to database
  │
  └── Publish message
          │
          ▼
       RabbitMQ
          │
          ▼
       Response
```

The API should **not send the email itself**.

It should return after the notification has been queued.

Example:

```text
HTTP 202 Accepted
```

This demonstrates the difference between **request processing** and **background processing**.

---

# 7. RabbitMQ Design

Use one exchange:

```text
notifications
```

Exchange type:

```text
direct
```

Routing key:

```text
notification.email
```

Queue:

```text
notification.email
```

Dead-letter queue:

```text
notification.email.dlq
```

### Message

Don't put the entire database entity into the message.

Use a small message contract:

```json
{
  "notificationId": "7f4f6e5e-2f4d-4e2d-a3d1-8a8d7c1a1234",
  "recipient": "user@example.com"
}
```

The consumer can retrieve the notification from the database.

---

# 8. Background Consumer

The consumer runs as an ASP.NET Core hosted service.

Conceptually:

```text
BackgroundService
       │
       ▼
RabbitMQ
       │
       ▼
Receive message
       │
       ▼
Load notification
       │
       ▼
Set status = Processing
       │
       ▼
Send notification
       │
       ├───────────────┐
       │               │
     success          failure
       │               │
       ▼               ▼
     Sent            Retry
```

Use:

```csharp
BackgroundService
```

with dependency injection.

The consumer should create a scoped service when it needs to access EF Core because `DbContext` is normally scoped.

---

# 9. Notification Provider

Don't put email-specific logic directly inside the consumer.

Create:

```csharp
public interface INotificationProvider
{
    Task SendAsync(
        Notification notification,
        CancellationToken cancellationToken);
}
```

Then:

```text
INotificationProvider
        │
        ▼
EmailNotificationProvider
```

Later:

```text
INotificationProvider
        ├── EmailNotificationProvider
        ├── SmsNotificationProvider
        └── PushNotificationProvider
```

For the MVP, use a fake provider instead of a real email service.

For example:

```text
FakeEmailProvider
```

which simply logs:

```text
Sending email to user@example.com
```

This keeps the project focused on backend architecture.

---

# 10. Retry Strategy

If sending fails, retry a limited number of times.

Example:

```text
Attempt 1 → failure
Attempt 2 → failure
Attempt 3 → failure
Attempt 4 → success
```

Maximum:

```text
3 retries
```

Use exponential backoff:

```text
1 second
2 seconds
4 seconds
```

After the final failure:

```text
Failed
   ↓
Dead Letter Queue
```

The important interview concept here is that **transient failures should not immediately become permanent failures**.

---

# 11. RabbitMQ Acknowledgement

The consumer should acknowledge the message only after successful processing.

```text
Receive message
      │
      ▼
Process
      │
      ├── Success → ACK
      │
      └── Failure → retry/requeue
```

This prevents a notification from being lost if the worker crashes during processing.

Discuss in the interview:

> What happens if the worker crashes after sending the email but before acknowledging the RabbitMQ message?

This exposes an important limitation:

### At-least-once delivery

The system may process a message more than once.

Therefore, the notification processing should eventually become **idempotent**.

---

# 12. Idempotency

Use the notification ID as the unique identifier.

Before processing:

```text
if notification.Status == Sent
    don't send again
```

This prevents a successfully processed notification from being sent again if RabbitMQ redelivers the message.

However, there is still a small failure window:

```text
Send email
   ↓
Email successfully sent
   ↓
Application crashes
   ↓
RabbitMQ message not ACKed
   ↓
Message delivered again
```

A production-grade notification system would need stronger provider-level idempotency or an outbox/idempotency strategy.

For this project, **recognizing and explaining this limitation is more important than trying to completely solve it**.

---

# 13. Database

Use SQL Server with EF Core.

### Notification table

```text
Notifications
------------------------------------------------
Id              uniqueidentifier PK
Recipient       nvarchar(320)
Subject         nvarchar(255)
Message         nvarchar(max)
Status          int
RetryCount      int
CreatedAt       datetime2
ProcessedAt     datetime2 NULL
LastError       nvarchar(max) NULL
```

Useful indexes:

```text
IX_Notifications_Status
IX_Notifications_CreatedAt
```

---

# 14. Error Handling

Use global exception middleware.

Instead of controllers returning random error formats:

```json
{
  "error": "Something went wrong"
}
```

Use a consistent problem-details response.

ASP.NET Core's:

```text
ProblemDetails
```

is suitable for this.

Expected errors should return appropriate HTTP status codes:

```text
400 → validation error
404 → notification doesn't exist
202 → notification accepted
500 → unexpected server error
```

---

# 15. Logging

Use structured logging.

Example:

```text
Notification {NotificationId} queued
Notification {NotificationId} processing
Notification {NotificationId} sent
Notification {NotificationId} failed
```

Don't log sensitive notification content unnecessarily.

Especially avoid:

```text
recipient
message body
passwords
tokens
```

unless there's a specific reason.

---

# 16. Configuration

Use configuration rather than hardcoding RabbitMQ/database settings.

Example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  }
}
```

Use environment variables for deployment.

---

# 17. Docker

Run the dependencies using Docker Compose:

```text
┌─────────────────────┐
│ ASP.NET API         │
├─────────────────────┤
│ SQL Server          │
├─────────────────────┤
│ RabbitMQ            │
└─────────────────────┘
```

The project should be runnable with:

```bash
docker compose up
```

RabbitMQ Management UI can be exposed for debugging.

---

# 18. Testing

## Unit tests

Test:

### NotificationService

```text
CreateNotification_ShouldCreateQueuedNotification
CreateNotification_ShouldPublishMessage
GetNotification_ShouldReturnNotification
```

### Consumer

```text
SuccessfulDelivery_ShouldMarkNotificationAsSent
FailedDelivery_ShouldIncreaseRetryCount
AlreadySentNotification_ShouldNotBeSentAgain
```

## Integration tests

Test the API:

```text
POST /api/notifications
```

Verify:

```text
HTTP 202
notification exists in database
```

You don't need a huge test suite.

Around **10–15 good tests** are enough for this project.

---

# 19. MVP Implementation Order

Don't build everything at once.

### Step 1 — API

Create:

```text
POST /api/notifications
GET /api/notifications/{id}
```

with EF Core.

### Step 2 — RabbitMQ

Publish a message after creating the notification.

### Step 3 — Consumer

Create the `BackgroundService` that consumes messages.

### Step 4 — Fake Provider

Implement:

```text
FakeEmailNotificationProvider
```

and mark notifications as `Sent`.

### Step 5 — Retry

Add:

```text
RetryCount
LastError
```

and retry failed messages.

### Step 6 — Dead Letter Queue

Configure a DLQ for permanently failed messages.

### Step 7 — Tests

Add unit and integration tests.

### Step 8 — Docker Compose

Containerize:

```text
API
SQL Server
RabbitMQ
```

---

# 20. Stretch Goals

Only add these after the MVP works.

### Outbox Pattern

Instead of:

```text
DB save
   ↓
RabbitMQ publish
```

use an outbox:

```text
DB transaction
 ├── Notification
 └── OutboxMessage
          ↓
     Publisher Worker
          ↓
       RabbitMQ
```

This solves the classic problem where the database succeeds but RabbitMQ publishing fails.

### Multiple channels

Add:

```text
Email
SMS
Push
```

with different providers.

### Priority

Support:

```text
High
Normal
Low
```

### Scheduled notifications

```text
POST /api/notifications
{
    "scheduledAt": "2026-10-07T15:00:00Z"
}
```

### Metrics

Track:

```text
notifications_sent
notifications_failed
notifications_retried
processing_duration
```

---

# 21. Interview Questions This Project Prepares You For

You should be able to explain:

### ASP.NET Core

* Why use `BackgroundService`?
* What is dependency injection?
* Why is `DbContext` scoped?
* How does middleware work?
* Why use DTOs?
* Why return `202 Accepted`?

### RabbitMQ

* What is an exchange?
* What is a queue?
* What is a routing key?
* What is an acknowledgment?
* What happens when a consumer crashes?
* What is a dead-letter queue?
* What is at-least-once delivery?

### Database

* Why use an index?
* What happens with concurrent updates?
* Why use transactions?
* What is the Outbox Pattern?

### Distributed systems

* What happens if RabbitMQ is unavailable?
* What happens if the email provider is down?
* How do you retry safely?
* How do you prevent duplicate notifications?
* How would you scale the consumers?

---

# 22. Definition of Done

The project is finished when this works:

```text
POST /api/notifications
        │
        ▼
SQL Server
        │
        ▼
RabbitMQ
        │
        ▼
BackgroundService
        │
        ▼
Fake Email Provider
        │
        ▼
Status = Sent
```

And you can demonstrate:

```text
Successful notification
        ↓
Queued → Processing → Sent
```

and:

```text
Failed notification
        ↓
Queued → Processing → Retry
                         ↓
                       Retry
                         ↓
                     Failed/DLQ
```

The **MVP should stay small**. Don't add authentication, React, microservices, Kubernetes, or a real email provider unless you finish the core system first.

The real value of this project is being able to sit in an interview and confidently explain **why each component exists and what happens when something fails**.
