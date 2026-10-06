# -----------------------------------------------------------------------------
# Sprk.Provisioning.ControlPlane.Sidecar — Exchange mailbox-access sidecar image
# (H14a: group-scoped RBAC for Applications since task 251; T4 action-and-verify).
#
# Runtime model (design.md §4.2a, DS-1b §3):
#   ONE sitecontainer on the same App Service as Sprk.Provisioning.ControlPlane
#   .Api / .Worker. Same Plan, same UAMI, same App Insights, same Log Analytics
#   workspace. Zero new Azure resources beyond one ACR repo tag.
#
# Why this image exists at all (multi-year residual, DS-1b §0 — verified
# 2026-08-18 against Microsoft Learn):
#   No Graph API exists for ApplicationAccessPolicy management, and none exists
#   for its designated successor RBAC-for-Applications either — both are
#   EXO-PowerShell-only management surfaces. This is not a stopgap: the
#   sidecar's IExchangePolicyApplier seam is the R22 migration point for the
#   day (if ever) Microsoft ships a REST equivalent.
#
# Attack surface = .NET runtime + pwsh + one signed Microsoft PowerShell module.
# Deliberately absent: Az / Az.* modules, Azure CLI, Power Platform CLI,
# Python, Node, and the .NET SDK (present only in the discarded build stage).
#
# Size: ceiling 250 MB compressed, enforced by the CI workflow's size step.
# -----------------------------------------------------------------------------

# =============================================================================
# WHY THIS IS NOT A `mcr.microsoft.com/powershell:*` BASE  (rewritten 2026-08-27)
# =============================================================================
# The image previously used `mcr.microsoft.com/powershell:7.4-mariner-2.0`, whose
# header claimed "no .NET runtime" and "security-patched monthly". Both had
# stopped being true:
#
#   * It DID ship a .NET runtime. PowerShell 7 IS a .NET application, so the base
#     necessarily carries one — .NET 8.0.10, months behind. Trivy found 28
#     HIGH/CRITICAL there, and essentially nowhere else. The "Trivy surface
#     minimization" comment listed `dotnet` as excluded while it was in fact the
#     entire vulnerability surface.
#   * Mariner 2.0 is END OF LIFE. MCR publishes a `7.4-mariner-2.0-EOL` tag
#     beside the one we pinned. An EOL base accumulates unfixed CVEs by
#     definition, so the monthly rebuild cadence was pulling nothing.
#
# The obvious fix — bump to `powershell:7.4-azurelinux-3.0` — buys very little.
# The PowerShell base lines map onto .NET releases:
#
#     pwsh 7.4  ->  .NET 8   (EOL 2026-11-10 — weeks away; the very deadline
#                             that drove this repo's dotnet-10-upgrade-r1)
#     pwsh 7.5  ->  .NET 9   (STS, an even shorter runway)
#     pwsh 7.6  ->  .NET 10  (LTS) — NO base image published for it
#
# So every available `powershell:*` tag lands on a runtime that is already EOL or
# about to be, and we would be back here within months — the exact churn this
# change is meant to end.
#
# Instead: base on the .NET 10 LTS runtime image directly and install PowerShell
# 7.6 as a dotnet tool on top. That inverts the dependency — we pick the runtime
# (supported to Nov 2028) and layer pwsh onto it, rather than accepting whichever
# runtime a pwsh base image happens to carry. It also puts the sidecar on the
# SAME .NET 10 LTS the rest of the estate migrated to, so there is one runtime
# story instead of two.
#
# Revisit when Microsoft publishes a `powershell:7.6-azurelinux-3.0` base: at
# that point a single-stage FROM may be simpler than this two-stage build. It is
# not a correctness issue either way.
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1 — acquire PowerShell as a dotnet tool.
#
# `dotnet tool install` is an SDK command, so it needs the SDK image; the SDK is
# discarded with this stage and never reaches the final image. --tool-path (not
# --global) keeps the install location explicit rather than $HOME-dependent, so
# it does not matter which user the runtime image runs as.
#
# PINNED, never floating — same supply-chain rule the EXO module pin follows.
# The package is a signed Microsoft NuGet artifact.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS pwsh-tool

ARG PWSH_VERSION=7.6.5

RUN dotnet tool install --tool-path /opt/powershell PowerShell --version ${PWSH_VERSION}

# -----------------------------------------------------------------------------
# Stage 2 — runtime image.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0

COPY --from=pwsh-tool /opt/powershell /opt/powershell

# pwsh on PATH for the ENTRYPOINT and for the module install below.
ENV PATH="/opt/powershell:${PATH}"

# Modules live at a fixed path we own, rather than inside the tool's
# version-stamped .store/ directory (which would move on every PWSH_VERSION bump).
# PowerShell adds its own $PSHOME modules to the search path at startup, so
# setting this does not hide the built-ins.
ENV PSModulePath="/opt/spaarke/psmodules"

# -----------------------------------------------------------------------------
# ExchangeOnlineManagement module — PINNED (never `latest`).
#
# Pin rationale: the sidecar relies on a stable Connect-ExchangeOnline parameter
# surface -- specifically -AccessToken (module 3.1.0+): since task 251 the sidecar
# holds no credential and connects with the token the Worker sends per request
# (owner D24). Any pin bump is a coordinated PR, NOT a passive `Update-Module` at
# build time.
#
# Pin: 3.5.1 (2024-05 release; -AccessToken supported).
# -----------------------------------------------------------------------------
ARG EXO_MODULE_VERSION=3.5.1

RUN mkdir -p /opt/spaarke/psmodules \
    && pwsh -NoProfile -NonInteractive -Command \
        "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; \
         Save-Module -Name ExchangeOnlineManagement \
                     -RequiredVersion ${EXO_MODULE_VERSION} \
                     -Path /opt/spaarke/psmodules \
                     -Force"

# -----------------------------------------------------------------------------
# BUILD-TIME CONTRACT CHECK — added 2026-08-27 with the base change.
#
# The two things this rewrite could plausibly break are module RESOLUTION (the
# module path moved off the pwsh base image's /opt/microsoft/... layout) and
# EXO 3.5.1's COMPATIBILITY with pwsh 7.6. Both would otherwise surface only at
# runtime, inside a sitecontainer, during a real provisioning run — the worst
# place to discover them.
#
# Asserting here turns both into a red CI build instead. This does NOT replace
# the task-162 live verification, which is the only thing that can prove
# Connect-ExchangeOnline actually authenticates against real Exchange; it just
# means a broken image cannot reach that stage.
# -----------------------------------------------------------------------------
RUN pwsh -NoProfile -NonInteractive -Command \
        "\$ErrorActionPreference = 'Stop'; \
         Import-Module ExchangeOnlineManagement; \
         if (-not (Get-Command Connect-ExchangeOnline -ErrorAction SilentlyContinue)) { \
             throw 'Connect-ExchangeOnline not found after Import-Module'; \
         } \
         \$p = (Get-Command Connect-ExchangeOnline).Parameters; \
         if (-not \$p.ContainsKey('AccessToken')) { \
             throw '-AccessToken parameter absent; the sidecar sign-in depends on it'; \
         } \
         Write-Host \"OK: pwsh \$(\$PSVersionTable.PSVersion), EXO module resolved, -AccessToken present\""

# -----------------------------------------------------------------------------
# Application layout
# -----------------------------------------------------------------------------
WORKDIR /app

# The sidecar's logic (task 251): settings, the Exchange connection and the
# RBAC-for-Applications apply/read operations H14a and H13 T4 call.
COPY SidecarCore.psm1 /app/SidecarCore.psm1

# The pwsh HTTP listener — the container's main process.
# Binds 127.0.0.1:8091 (localhost-only per DS-1b §3 auth leg #1). Sitecontainers
# share the App Service network namespace, so localhost is reachable from the
# main .NET app; the port is NOT publicly routed by App Service's front-end
# unless someone explicitly configures it (do not).
COPY Listener.ps1 /app/Listener.ps1

# -----------------------------------------------------------------------------
# Sitecontainer port declaration (informational — App Service sitecontainers
# use the network namespace directly; EXPOSE does not open a public port).
# -----------------------------------------------------------------------------
EXPOSE 8091

# -----------------------------------------------------------------------------
# Health / readiness note: GET /healthz returns 200 once HttpListener has bound
# the port -- "ok", or "degraded: <missing settings>". The listener binds even
# when a setting is missing (task 251 / G30: a sidecar that refuses to start
# holds the whole Worker site at 503).
# -----------------------------------------------------------------------------

# ENTRYPOINT runs Listener.ps1 as PID 1. Non-interactive; -NoProfile keeps
# startup deterministic (no user profile evaluation).
ENTRYPOINT ["pwsh", "-NoProfile", "-NonInteractive", "-File", "/app/Listener.ps1"]
