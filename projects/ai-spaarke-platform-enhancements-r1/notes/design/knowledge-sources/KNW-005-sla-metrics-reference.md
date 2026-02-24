# KNW-005 — SLA Metrics Reference

> **External ID**: KNW-005
> **Content Type**: Reference
> **Tenant**: system
> **Created**: 2026-02-23
> **Task**: AIPL-032

---

## Overview

This reference document defines standard Service Level Agreement (SLA) metrics, measurement methodologies, typical values by service category, and common SLA structure patterns. It is designed to support AI-assisted review of managed service agreements, cloud service SLAs, software maintenance agreements, and other service contracts that include measurable performance commitments.

---

## Part 1: Core SLA Concepts

### 1.1 Service Level Objective (SLO) vs. Service Level Agreement (SLA)

- **Service Level Objective (SLO)**: An internal target that a service provider sets for itself. SLOs are not contractually binding but are used as operational targets. Example: "We target 99.9% availability."
- **Service Level Agreement (SLA)**: A contractual commitment to a defined performance level. Breaching an SLA triggers a specified remedy (typically service credits). Example: "We guarantee 99.9% availability; breaches earn service credits per the SLA schedule."
- **Service Level Indicator (SLI)**: The specific measurement used to determine whether the SLO or SLA target is met. Example: "Availability is measured as: (total minutes in period − downtime minutes) / total minutes in period."

### 1.2 Service Credits

Service credits are the standard remedy for SLA breaches. Credits are applied against future invoices and are typically expressed as:
- A percentage of monthly service fees
- A fixed dollar amount per incident
- A percentage based on the duration of downtime (tiered)

Service credits are typically the exclusive remedy for SLA breaches and do not constitute acknowledgment of liability beyond the credit amount.

---

## Part 2: Availability Metrics

### 2.1 Availability Definition

Availability is the most common SLA metric. It measures the percentage of time a service is operational and accessible.

**Standard formula**:
```
Availability % = ((Total Minutes − Downtime Minutes) / Total Minutes) × 100
```

Where:
- **Total Minutes**: The total number of minutes in the measurement period (month: 43,200; year: 525,600)
- **Downtime Minutes**: Minutes during which the service is unavailable due to unplanned outages (not including scheduled maintenance)

### 2.2 Common Availability Tiers and Allowable Downtime

| Availability | Annual Downtime | Monthly Downtime | Category |
|---|---|---|---|
| 99.0% ("two nines") | 87.6 hours | 7.2 hours | Basic |
| 99.5% | 43.8 hours | 3.6 hours | Standard |
| 99.9% ("three nines") | 8.76 hours | 43.8 minutes | Enhanced |
| 99.95% | 4.38 hours | 21.9 minutes | High Availability |
| 99.99% ("four nines") | 52.6 minutes | 4.38 minutes | Mission Critical |
| 99.999% ("five nines") | 5.26 minutes | 26.3 seconds | Ultra-High Availability |

### 2.3 Scheduled Maintenance Exclusions

Most SLAs exclude scheduled maintenance windows from downtime calculations. Standard provisions:
- Advance notice required: Typically 48–72 hours for standard maintenance; 2 hours for emergency maintenance
- Maintenance window timing: Off-peak hours (e.g., 00:00–06:00 UTC on weekends)
- Maximum maintenance window duration: Typically 4–8 hours per month

### 2.4 Force Majeure and Exclusions

SLAs commonly exclude the following from downtime calculations:
- Force majeure events
- Customer-caused outages (customer's network, misconfiguration)
- Third-party service dependencies outside the provider's control
- Scheduled maintenance windows
- Outages during periods of suspended service due to non-payment

---

## Part 3: Performance Metrics

### 3.1 Response Time / Latency

Response time measures how quickly the service responds to requests.

**Common definitions**:
- **p50 (median)**: 50% of requests complete within this time — typical experience
- **p95**: 95% of requests complete within this time — reflects most users' experience
- **p99**: 99% of requests complete within this time — captures tail latency affecting a minority of users
- **p99.9**: 99.9% of requests complete within this time — used for strict SLAs on mission-critical operations

**Typical SLA targets by service type**:

| Service Type | p95 Target | p99 Target |
|---|---|---|
| REST API (read) | < 200ms | < 500ms |
| REST API (write) | < 500ms | < 1,000ms |
| Database query | < 100ms | < 300ms |
| File download (1MB) | < 2s | < 5s |
| Web page load | < 3s | < 6s |
| Batch processing | Varies by job size | Varies |

### 3.2 Throughput

Throughput measures the volume of work a service can process per unit of time.

**Common metrics**:
- **Requests per second (RPS)**: Number of API calls or transactions processed per second
- **Transactions per second (TPS)**: For payment or transactional systems
- **Messages per second (MPS)**: For messaging or event streaming systems
- **Data transfer rate (Mbps/Gbps)**: For storage or network services

SLAs may define minimum throughput guarantees and burst capacity.

### 3.3 Error Rate

Error rate measures the percentage of requests that result in errors.

**Common targets**:
- Standard services: < 0.1% error rate
- High-availability services: < 0.01% error rate

Errors are typically classified by HTTP status code:
- **4xx errors** (client errors): Usually excluded from provider SLA calculations
- **5xx errors** (server errors): Counted as provider-caused errors for SLA purposes

---

## Part 4: Support and Incident Response Metrics

### 4.1 Incident Severity Levels

SLAs typically define incident severity to determine response and resolution time commitments:

| Severity | Definition | Example |
|---|---|---|
| P1 / Critical | Complete service outage; no workaround available | Production system down; all users affected |
| P2 / High | Significant degradation; major functionality impaired; partial workaround available | Core feature unavailable; 50%+ users affected |
| P3 / Medium | Moderate impact; workaround available | Non-critical feature unavailable; small user segment affected |
| P4 / Low | Minor issue; cosmetic or informational | UI display issue; documentation error |

### 4.2 Response Time Commitments

| Severity | Initial Response | Status Update Frequency | Target Resolution |
|---|---|---|---|
| P1 / Critical | 15–30 minutes | Every 30–60 minutes | 4 hours |
| P2 / High | 1–2 hours | Every 2–4 hours | 8–24 hours |
| P3 / Medium | 4–8 hours (business hours) | Daily | 3–5 business days |
| P4 / Low | 1–2 business days | Weekly | Next release or best effort |

**Initial response**: Acknowledgment that the incident has been received and assigned; does not mean resolution has begun.

### 4.3 Time to Restore Service (TTRS)

Also called Mean Time to Recover (MTTR). This metric measures the average time from incident detection to service restoration.

---

## Part 5: Data and Storage Metrics

### 5.1 Data Durability

Data durability measures the probability that stored data will not be lost.

**Typical values**:
- Standard object storage: 99.999999999% (11 nines) annual durability
- Database backups: 99.99999% (7 nines) annual durability
- Long-term archival: 99.9% annual durability

### 5.2 Recovery Point Objective (RPO)

RPO defines the maximum amount of data loss (measured in time) acceptable in a disaster recovery scenario.

**Example**: An RPO of 4 hours means that in a disaster, up to 4 hours of data may be lost.

**Common RPO tiers**:
- Mission critical: < 15 minutes
- Business critical: 1–4 hours
- Standard: 24 hours
- Archival: 7 days

### 5.3 Recovery Time Objective (RTO)

RTO defines the maximum time allowed to restore service after a disaster.

**Common RTO tiers**:
- Mission critical: < 1 hour
- Business critical: 4–8 hours
- Standard: 24–48 hours
- Non-critical: Best effort

---

## Part 6: SLA Credit Schedule Structure

### 6.1 Tiered Credit Structure

SLA credits are typically structured in tiers based on the severity of the breach:

| Availability Achieved | Monthly Credit (% of Monthly Fees) |
|---|---|
| 99.0% – 99.9% (if SLA is 99.9%) | 10% |
| 95.0% – 99.0% | 25% |
| 90.0% – 95.0% | 50% |
| < 90.0% | 100% |

### 6.2 Credit Caps and Exclusive Remedy

SLA credits are commonly:
- Capped at 100% of monthly fees per month
- Stated to be the sole and exclusive remedy for SLA breaches
- Applied as credits against future invoices (not cash refunds)
- Subject to a claim filing deadline (typically within 30 days of the incident)

### 6.3 Credit Claim Procedure

To claim service credits, customers typically must:
1. Submit a written credit request within the claim window
2. Include the dates and times of the downtime event
3. Provide supporting evidence (monitoring logs, error messages)

---

## Part 7: Measurement and Reporting

### 7.1 Measurement Period

SLA compliance is typically measured monthly. Annual aggregation is common for reporting but credits are usually calculated on a monthly basis.

### 7.2 Monitoring Methodology

SLAs should specify who is responsible for monitoring, and how:
- **Provider monitoring**: The service provider measures availability using its internal monitoring tools
- **Third-party monitoring**: An independent monitoring service measures availability
- **Synthetic monitoring**: Automated tests simulate user traffic to measure availability and response time
- **Real user monitoring (RUM)**: Actual user transactions are measured

### 7.3 Availability Report

Providers are typically obligated to publish monthly availability reports accessible to customers. Reports should include:
- Actual availability percentage for the period
- List of incidents with start time, end time, and impact
- Comparison against SLA target

---

*This reference document supports AI-assisted SLA analysis. It does not constitute legal advice. Specific SLA terms and credit structures vary significantly by vendor, service type, and negotiation.*
