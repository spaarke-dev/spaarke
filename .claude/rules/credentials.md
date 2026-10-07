---
paths:
  - "scripts/Seed-*KeyVault*.ps1"
  - "scripts/provisioning/**"
  - "scripts/Rotate-Secrets.ps1"
  - "scripts/*SpeConfigSecret*.ps1"
  - "scripts/Provision-Customer.ps1"
  - "scripts/Decommission-Customer.ps1"
  - "scripts/Register-EntraAppRegistrations.ps1"
  - "scripts/Configure-ProductionAppSettings.ps1"
  - "scripts/canonical-secret-catalog/**"
  - "infrastructure/bicep/**"
  - "src/server/services/Sprk.Provisioning.ControlPlane.*/**"
  - "src/server/api/Sprk.Bff.Api/Infrastructure/Graph/**"
---

# Credentials and Key Vault secrets — root CLAUDE.md §9

This code creates, seeds, rotates or reads credentials. Before changing how a secret is created, seeded, rotated, deleted or purged, or how an identity's credential order is set, read the **current** text of:

- `.claude/constraints/provisioning.md` — "KV credential lifecycle". It has three environment-specific prongs and a time-boxed hold (sunset 2026-11-23).
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — Amendment A4 (secret-free BFF identity) and exceptions E-1 to E-3.

Apply the documents' current wording, not a summary of it. These rules change as the managed-identity migration proceeds, and an out-of-date paraphrase is how a rollback copy gets deleted or a secret gets reintroduced.
