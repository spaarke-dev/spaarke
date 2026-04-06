# Task Index — spaarke-environment-provisioning-app

> **Total Tasks**: 17
> **Last Updated**: 2026-04-06

## Status Overview

| # | Title | Phase | Status | Dependencies |
|---|-------|-------|--------|-------------|
| 001 | Create DataverseEnvironment Schema Script | 1 | ✅ | — |
| 002 | Run Schema Script Against Dev | 1 | 🔲 | 001 |
| 003 | Create Environment Entity Form | 1 | 🔲 | 002 |
| 004 | Create Environment Entity Views | 1 | 🔲 | 002 |
| 005 | Add Sitemap Entry | 1 | 🔲 | 002 |
| 006 | Add Environment Lookup to Registration | 1 | 🔲 | 002 |
| 007 | Seed Dev and Demo 1 Records | 1 | 🔲 | 002 |
| 010 | Create DataverseEnvironmentRecord DTO | 2 | ✅ | — |
| 011 | Create DataverseEnvironmentService | 2 | ✅ | 010 |
| 012 | Register Service in DI | 2 | ✅ | 011 |
| 013 | Refactor ApproveRequest Endpoint | 2 | ✅ | 012 |
| 014 | Handle Malformed License JSON | 2 | ✅ | 013 |
| 015 | Refactor DemoProvisioningService | 2 | 🔲 | 013 |
| 016 | Update Admin Email Source | 2 | 🔲 | 015 |
| 020 | Remove Environment Picker from Ribbon JS | 3 | ✅ | 013 |
| 021 | Add Lookup Validation for Form Approve | 3 | ✅ | 020 |
| 022 | Add Per-Record Validation for Grid Approve | 3 | ✅ | 020 |
| 030 | Remove Environments from Options | 4 | 🔲 | 016 |
| 031 | Refactor RegistrationDataverseService URL | 4 | 🔲 | 030 |
| 032 | Update Documentation | 4 | 🔲 | 031 |
| 040 | E2E Testing Checklist | 5 | 🔲 | 032 |
| 090 | Project Wrap-Up | 5 | 🔲 | 040 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 003, 004, 005, 006, 007 | 002 complete | Independent Dataverse customizations |
| B | 014, 015 | 013 complete | Independent endpoint changes |
| C | 020 (then 021, 022) | 013 complete | Ribbon JS — sequential within group |
| D | 021, 022 | 020 complete | Independent JS validation functions |

## Critical Path

```
001 → 002 → 006 → 010 → 011 → 012 → 013 → 015 → 016 → 030 → 031 → 032 → 040 → 090
```

## Risk Items

1. **RegistrationDataverseService URL refactor (031)** — Currently hardwired to default env URL. Significant change to thread-safe token management.
2. **Lookup creation (006)** — Must use RelationshipDefinitions endpoint. Integer DefaultValue causes API errors.
3. **DemoExpirationService** — Existing DI issue. Must not break further during cleanup.
