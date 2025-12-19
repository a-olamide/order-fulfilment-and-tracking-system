# Order Fulfillment System (Event-Driven Microservices)

This project demonstrates a **production-style, event-driven order fulfillment system** built with **.NET 8**, **Kafka**, **PostgreSQL**, and **Elasticsearch**. It models the full lifecycle of an order from creation to payment processing, fulfillment scheduling, and search/indexing.

The system is intentionally designed to reflect **real-world backend engineering practices** used in large-scale platforms (e.g. Tesla Digital Products): asynchronous workflows, loose coupling, idempotency, observability, and scalability.

---

## 🧱 Architecture Overview

```
<img width="1224" height="640" alt="image" src="https://github.com/user-attachments/assets/085ea844-e181-4aff-b789-643803887f76" />

```

Each service is independently deployable and communicates **only via events**, enabling horizontal scalability and fault isolation.

---

## 🧩 Services

### 1. OrderService

* ASP.NET Core REST API
* Persists orders using **EF Core + PostgreSQL**
* Publishes `OrderCreatedV1` events to Kafka
* Generates `CorrelationId` for end-to-end tracing

Endpoint:

```
POST /orders
```

---

### 2. PaymentWorker

* Kafka consumer (`orders.events.v1`)
* Simulates payment authorization / failure
* Publishes `PaymentAuthorizedV1` or `PaymentFailedV1`
* Uses **idempotent event handling** (processed-event table)

---

### 3. FulfillmentWorker

* Kafka consumer (`payments.events.v1`)
* Reacts only to successful payments
* Schedules fulfillment and publishes `FulfillmentScheduledV1`
* Ensures exactly-once logical processing via idempotency

---

### 4. SearchIndexerWorker

* Kafka consumer (all domain topics)
* Builds a **CQRS-style read model**
* Indexes/upserts order state into **Elasticsearch**
* Maintains an event timeline per order

Elasticsearch index:

```
orders-v1
```

---

## 🛠️ Technology Stack

* **.NET 8 / C#**
* **Apache Kafka (KRaft mode)**
* **PostgreSQL**
* **Elasticsearch 8.x + Kibana**
* **Docker & Docker Compose**
* **EF Core**

---

## ▶️ Running the System

### 1. Start infrastructure

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Ensure the following containers are running:

* Kafka
* PostgreSQL
* Elasticsearch
* Kibana

---

### 2. Run services

In separate terminals:

```bash
dotnet run --project src/OrderService
```

```bash
dotnet run --project src/PaymentWorker
```

```bash
dotnet run --project src/FulfillmentWorker
```

```bash
dotnet run --project src/SearchIndexerWorker
```

---

### 3. Create an order

Via Swagger or curl:

```json
{
  "customerEmail": "user@example.com",
  "productType": "Phone",
  "country": "US",
  "paymentScenario": "success"
}
```

---

## 🔎 Observability & Debugging

### Kafka

List topics:

```bash
docker exec -it ofs-kafka kafka-topics --bootstrap-server localhost:9092 --list
```

Consume events:

```bash
docker exec -it ofs-kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic orders.events.v1 --from-beginning
```

---

### Elasticsearch

Query indexed orders:

```bash
curl http://localhost:9200/orders-v1/_search?pretty
```

Search by status:

```bash
curl -X POST http://localhost:9200/orders-v1/_search -H "Content-Type: application/json" -d '{"query":{"match":{"status":"FulfillmentScheduled"}}}'
```

---

## 🔐 Key Engineering Practices Demonstrated

* Event-driven microservices
* Loose coupling via Kafka
* Idempotent consumers
* Correlation & causation IDs
* CQRS-style read model
* Schema-versioned domain events
* Dockerized local development


