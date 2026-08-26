# Manual Gates — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Every gate (H0.5 admin consent, H1 quota-support-ticket, H8 SPE-container-type 24h, H11 user-bootstrap, or any escalation) writes here.
> Never delete prior entries — corrections + reversals append with rationale.

## Gate log

### Gate — {handlerId} ({gateName})

- **Opened**: `{ts}` (source: `/provision-environment` skill Step 5 or handler self-report)
- **What operator was asked to do**: {precise instruction — URL, command, confirmation phrase}
- **What operator did**: {action taken OR "abandoned" OR "delegated to {upn}"}
- **Closed**: `{ts}` (source: {re-verification via L2 API / operator confirmation / handler retry})
- **Outcome**: `{Success | Failed | Cancelled | Deferred-to-follow-up-run}`
- **Escalation decision + rationale** (if applicable): {per root CLAUDE.md §6 or §6.5 — cite path A/B/C when ADR conflict}

---

### Example gate entry (for reference)

### Gate — H0.5 (Multitenant BFF app admin-consent)

- **Opened**: `2026-09-01T10:12Z`
- **What operator was asked to do**: Navigate to `https://login.microsoftonline.com/{tenantId}/adminconsent?client_id={bffClientId}&redirect_uri={redirectUri}` and grant consent as a tenant-admin identity. Return the confirmation URL.
- **What operator did**: Granted as `admin@customer.onmicrosoft.com`. Returned redirect URL with `admin_consent=True`.
- **Closed**: `2026-09-01T10:15Z` (re-verified via Graph `GET /servicePrincipals?filter=appId eq '{bffClientId}'` — sp present in customer tenant with 3 required delegations granted)
- **Outcome**: `Success`
- **Escalation decision + rationale**: n/a (nominal flow)

---

### Example ADR-conflict escalation

### Gate — Escalation: H4 secret rotation conflicts with ADR-028 client-cred rotation cadence

- **Opened**: `2026-09-01T10:34Z`
- **ADR in question**: ADR-028 § "Client-cred rotation cadence"
- **Specific rule**: MUST rotate client-cred no more than once per 90 days
- **Conflict**: H4 detected secret drift (source-service key rotated 15 days ago); strict compliance means H4 would refuse to update the KV secret, leaving BFF pointing at a stale password → auth failure at Step 5.
- **Proposed path**: A (project-scoped exception — this is a drift-detection recovery, not a scheduled rotation)
- **Rationale**: The 90-day cadence bounds SCHEDULED rotations to limit key-material churn; drift-detection recovery is a different failure mode outside the cadence's intent. See `.claude/patterns/provisioning/manifest-driven-secret-catalog.md` for drift-vs-rotation distinction.
- **Impact if path A accepted**: KV secret `{secret-name}` updated to match source-service value; ADR-028 cadence untouched.
- **Alternative considered (and rejected)**: Path C (comply) — would leave BFF broken. Path B (amendment) — heavy for a narrow edge case.
- **Operator decision**: `2026-09-01T10:38Z` — Path A approved by {upn}. Rotation applied. `handler-log.md` H4 row updated to `Success (drift-recovery per Gate #2)`.
