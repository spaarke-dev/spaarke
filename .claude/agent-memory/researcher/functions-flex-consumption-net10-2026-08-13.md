---
name: functions-flex-consumption-net10-2026-08-13
description: Azure Functions Flex Consumption DOES support .NET 10 isolated worker; Bicep functionAppConfig.runtime.version = "10.0"
metadata:
  type: project
---

# Azure Functions Flex Consumption + .NET 10 isolated (verified 2026-08-13)

**Verdict: SUPPORTED.** Flex Consumption supports `dotnet-isolated` on Functions 4.x for .NET 10.0, 9.0, 8.0. .NET 10 GA'd for Functions and .NET 10 isolated is explicitly the recommended plan (Consumption on Linux does NOT support .NET 10; use Flex).

**Bicep line edit for dotnet-10-upgrade-r1:**
`functionAppConfig.runtime = { name: 'dotnet-isolated', version: '10.0' }`
The version string is the plain numeric form `"10.0"` — same format as the existing `"8.0"` and as `az functionapp create --runtime dotnet-isolated --runtime-version 10.0`. In-process model gets NO .NET 10 and reaches EOS 2026-11-10, so isolated is mandatory.

**Why:** dotnet-10-upgrade-r1 is migrating a FlexConsumption Functions app 8.0 → 10.0.
**How to apply:** change one Bicep line to version '10.0'; no plan/SKU change needed.

**Watch-out (tooling, not platform):** Azure Functions Core Tools v4.7.0 had a regression (~Feb 2026) where publishing .NET 10 Isolated to Flex Consumption fails / `--dotnet-version 10.0` ignored (GitHub azure-functions-core-tools#4794); v4.6.0 worked. Verify Core Tools version if deploy pipeline uses func publish.

**Sources:**
- MicrosoftDocs/azure-docs includes/functions-dotnet-supported-versions.md (4.x isolated: .NET 10, 9.0, 8.0)
- learn.microsoft.com/azure/azure-functions/flex-consumption-how-to (runtime create examples; ms.date 2026-08-04)
- learn.microsoft.com/azure/azure-functions/functions-versions
- GitHub azure-functions-core-tools#4794 (Core Tools v4.7.0 Flex publish regression)

**Open:** Learn how-to code samples still show `--runtime-version 8.0`; confirm regional availability of 10.0 via `az functionapp list-flexconsumption-runtimes --location <region>` before deploy if a low-availability region is used.
