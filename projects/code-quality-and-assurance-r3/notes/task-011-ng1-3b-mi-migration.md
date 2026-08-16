# #3b — Dataverse `ClientSecret` → Managed Identity migration (routed to task 011 / NG1 / Idea #742)

> **Source**: r3 RED-4 Fable-verified assessment (`notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`)
> **Owner track**: task 011 / NG1 (Idea #742) — the assess-then-decide credential-migration slice.

## Scope (verified, separable, constructor-scoped)

ADR-028 §24 mandates Managed Identity for server-outbound Dataverse. **Both** Dataverse impls still use a
client secret and are in unremediated violation:
- `DataverseServiceClientImpl` — `AuthType=ClientSecret` connection string from `API_CLIENT_SECRET`
  (`DataverseServiceClientImpl.cs:42-65`). Migrate the connection to `DefaultAzureCredential` / MI token provider.
- `DataverseWebApiService` — `ClientSecretCredential` from `Dataverse:ClientSecret`
  (`DataverseWebApiService.cs:40,56`). Swap the `TokenCredential` to `DefaultAzureCredential`.

**Plan of record**: a third Dataverse camp (Services/Ai raw-HTTP) was already migrated to MI in **AUTHV2-042
Phase C** (`appsettings.template.json:80`), which explicitly gates full secret removal on this #3b slice and
names the MI (`mi-bff-api-{env}`) + the operator step (register the UAMI as a Dataverse **Application User**).

## Binding operator prerequisites (per env — dev only for now; demo/prod decommissioned)

1. Register `mi-bff-api-{env}` as a Dataverse **Application User** with the required security role.
2. Grant **`prvActOnBehalfOfAnotherUser`** to the MI's app-user — REQUIRED for the impersonated WRITE path
   (`CommunicationModule.cs:288`); impersonation regression tests belong in this task.
3. **Do NOT remove** `Dataverse-ClientSecret` / `API_CLIENT_SECRET` until MI attribution is proven LIVE
   (never-remove until then). Keep the secret path as fallback during cutover.

## Relationship to the other RED-4 pieces

- **Independent of** the interim hardening (`dataverse-access-hardening`) — MI migration is constructor-scoped.
- **Feeds** the `dataverse-access-unification-r1` project — the single-impl target is MI-only.
