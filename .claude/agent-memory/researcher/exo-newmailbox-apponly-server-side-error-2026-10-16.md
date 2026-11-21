---
name: exo-newmailbox-apponly-server-side-error-2026-10-16
description: EXO V3 generic "A server side error has occurred..." on app-only New-Mailbox -Shared (T186 L61) - message is server-originated, no documented cause, how to extract real cause, ranked hypotheses
metadata:
  type: reference
---

## 2026-10-16: New-Mailbox -Shared app-only "server side error" (T186)
**Question**: What does the generic EXO V3 "A server side error has occurred ... reach out to MS support" mean for app-only New-Mailbox -Shared with an RBAC-for-Apps custom role, and how to get the real cause?

**Findings**:
- The string is NOT in the client module (3.5.1 DLL/psm1 grep: absent) - it is the server's top-level OData error `message`. Module wraps it via generated `Write-ErrorMessage`; FullyQualifiedErrorId carries `[Server=..,RequestId=..,TimeStamp=..]`. A different failing case (Q&A 1023619, 2022) shows the raw JSON body has `error.innererror.internalexception.message` with the REAL cause; it surfaced as `TerminatingError(Invoke-WebRequest)` text (transcript/$Error). Inference: capture $Error[1..n].ErrorDetails.Message / Start-Transcript.
- No documented or widely reported cause for New-Mailbox specifically. Same text reported for: Set-Mailbox under MI in Durable Functions, writes fail/reads OK (Q&A 5899848, 2026-05; no root cause, workaround = separate PS session); PowerShell/PowerShell #23824 (client Newtonsoft JsonConvert.DefaultSettings pollution breaks all cmdlets but Connect; closed 2024-05); ServiceNow delete ops (no answer). Duplicate proxyAddress on a mail-enabled group blocked New-Mailbox -Shared but with a DESCRIPTIVE error (Q&A 5768031, 2026-02).
- Docs: app-only exclusions only UnifiedGroup + eDiscovery; New-Mailbox -Shared not named either way. App-only doc Options 2/3 (custom role group + service principal, Entra role combined) is the documented admin-cmdlet path; direct `-App` assignment of a non-Application admin role is not documented on the App-RBAC page (which lists only Application* roles) - works but off-doc.
- Logging: `-EnableErrorReporting -LogDirectoryPath -LogLevel All` writes EXOCmdletTelemetry CSV (default %TMP%\EXOCmdletTelemetry); UseRPSSession deprecated 3.9.2 / RPS retired - not a diagnostic path. Module 3.10.1 is current (3.10.0+ requires PS 7.6; 3.5.0-3.9.2 documented for PS 7.4+).

**Sources**: learn.microsoft.com/powershell/exchange/exchange-online-powershell-v2 (release notes, 2026-07-31); .../app-only-auth-powershell-v2 (2026-08-26); .../exchange/permissions-exo/application-rbac (2026-08-21); .../module/exchangepowershell/connect-exchangeonline; Q&A 5899848, 1023619, 5768031; github.com/PowerShell/PowerShell/issues/23824; rakhesh.com 2024-04-11 (replay AdminApi with Invoke-RestMethod)

**Follow-up 2026-10-16 (raw adminapi call: New-Mailbox -WhatIf = 403 CmdletAccessDeniedException; SP prop OverrideEnforceExoAppRbacPermissions=false)**: `Set-ServicePrincipal -OverrideEnforceExoAppRbacPermissions <bool>` (default False) and org-level `Set-OrganizationConfig -EnforceExoAppRbacPermissions <bool>` (Q&A 5951705 shows org value False, 2026-07-20) exist on Learn but BOTH descriptions are unfilled `{{ Fill ... Description }}` placeholders (Set-ServicePrincipal updated 2026-05-28; Set-OrganizationConfig 2026-08-08). No MC post, blog, or Q&A explains semantics; no staff answer. Meaning (enforce app-RBAC on SPs; override = per-SP exemption) is INFERRED from names only. Docs-supported app-only paths for admin cmdlets: Entra role (Exchange Administrator / Exchange Recipient Administrator) or custom role group with SP as member (app-only-auth-powershell-v2 Options 1-3; admin-api-authentication 2026-09-25 says same for adminapi).

**Open questions**: whether the role entry needs params beyond the 5 listed (bisect with full parent role); real cause unknown until innererror captured.
