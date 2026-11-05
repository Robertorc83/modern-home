# ADR 006: Outbox, SQS and SES for notifications

**Status:** accepted · **Date:** 2026-11-05

## Context

Booking, confirming, declining and cancelling a showing must notify the other party. Sending email inside the
request is slow and fragile; sending it after the commit without a record loses emails on a crash; sending it
before the commit emails people about showings that never happened.

## Decision

**Transactional outbox.** The scheduling handlers raise domain events; the Notifications module's handlers write
an `outbox` document (`type: email.send`, payload: to, subject, body). Because the dispatcher is in process and the
outbox enlists in the scoped Mongo session, the row is written **in the same transaction** as the showing. Either
both commit or neither.

**The Worker delivers.** A separate .NET `BackgroundService` process:

* With AWS enabled (`Aws:Enabled=true`): a relay claims unpublished rows with an atomic lease and publishes them to
  **SQS**; a consumer long polls the queue and sends through **SES**. The queue has a dead letter queue
  (`maxReceiveCount: 3`), so a poison message parks instead of looping.
* Without AWS: the same processor claims rows and delivers in process, logging the email. The app still works for
  a reviewer with nothing but Docker.

**Idempotent delivery.** `sentEmails` has a unique index on `outboxId`. A redelivered SQS message or a worker
retry after a crash finds the record and acknowledges without sending again. Rows that fail back off
exponentially up to five attempts and keep their last error, visible on the admin "Outbox and emails" page.

**LocalStack for development.** `docker-compose.yml` runs `localstack/localstack:4.12.0` (pinned: later images
may require an auth token; this one does not) with `deploy/localstack/init-aws.sh` creating the bucket, the queue,
the DLQ and the verified sender. The AWS SDK clients take a `ServiceURL` from configuration; real AWS is a
configuration change. LocalStack's `/_aws/ses` endpoint lists every email it accepted, which is how the demo shows
delivery.

## Consequences

* A reviewer sees an email queued the instant a booking commits, and delivered a second later.
* Notification delivery can be scaled, paused and inspected independently of the API.
* Two moving parts more than "just send it": the outbox collection and the Worker. Both are small and boring, which
  is the point.
