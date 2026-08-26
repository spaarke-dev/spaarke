# -----------------------------------------------------------------------------
# Sprk.Provisioning.ControlPlane.Sidecar — Exchange ApplicationAccessPolicy
# sidecar image (H14a; T4 action-and-verify).
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
# What this image deliberately does NOT contain (Trivy surface minimization):
#   - NO Az / Az.* modules
#   - NO Azure CLI (`az`)
#   - NO Power Platform CLI (`pac`)
#   - NO .NET SDK / runtime (`dotnet`)
#   - NO Python / Node
# Attack surface = pwsh runtime + one signed Microsoft PowerShell module.
#
# Size target (design.md §4.2a): ~200-230 MB compressed. Ceiling 250 MB.
# Not the rejected "~50 MB" aspiration — that floor is not achievable with
# the EXO module baked in, and DS-1b §3 explicitly rejects it.
#
# Build: monthly rebuild cadence (pwsh + one signed module — quiet CVE loop,
# not az CLI's Python tree). CI: task 115 authors the GitHub Actions workflow
# (Trivy-gated push to ACR).
# -----------------------------------------------------------------------------

# Base: pwsh 7.4 on Mariner 2.0 — signed Microsoft base image, security-patched
# monthly, no distro package manager surface beyond `tdnf` (Mariner default).
# Compressed size ~110-130 MB (per DS-1b §3 measurement).
FROM mcr.microsoft.com/powershell:7.4-mariner-2.0

# -----------------------------------------------------------------------------
# ExchangeOnlineManagement module — PINNED (never `latest`).
#
# Pin rationale: the sidecar's H14a script relies on stable Connect-ExchangeOnline
# parameter surface (specifically -Certificate accepting X509Certificate2).
# Any pin bump is a coordinated PR (task 115's CI pins the same version for
# supply-chain integrity), NOT a passive `Update-Module` at build time.
#
# Pin: 3.5.1 (2024-05 release, stable -Certificate mode support). CI (task 115)
# will verify the pinned version resolves on rebuild; a bump requires an
# explicit code change here + a smoke re-run of task 162's live verification.
# -----------------------------------------------------------------------------
ARG EXO_MODULE_VERSION=3.5.1

# Install to a well-known path so Listener.ps1 can Import-Module deterministically.
# Save-Module places the module at <path>/ExchangeOnlineManagement/<version>/.
# Adds ~70-90 MB compressed (per DS-1b §3 measurement).
RUN pwsh -NoProfile -NonInteractive -Command \
        "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; \
         Save-Module -Name ExchangeOnlineManagement \
                     -RequiredVersion ${EXO_MODULE_VERSION} \
                     -Path /opt/microsoft/powershell/7/Modules \
                     -Force"

# -----------------------------------------------------------------------------
# Application layout
# -----------------------------------------------------------------------------
WORKDIR /app

# The amended Set-ExchangeApplicationAccessPolicy.ps1 — copied from the sidecar
# directory (NOT the repo-root scripts/ copy). The sidecar copy has the
# ~10-line -Certificate amendment (Linux containers have no Windows cert store,
# so the -CertificateThumbprint mode the original script uses cannot resolve;
# see the script header for the diff-vs-original commentary).
COPY Set-ExchangeApplicationAccessPolicy.ps1 /app/Set-ExchangeApplicationAccessPolicy.ps1

# The ~60-line pwsh HTTP listener — the container's main process.
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
# Health / readiness note: the Listener itself has a GET /healthz endpoint
# returning 200 once HttpListener has bound the port. Task 162 (live verify)
# uses this; App Service sitecontainer health-probe config is task 108's
# concern.
# -----------------------------------------------------------------------------

# ENTRYPOINT runs Listener.ps1 as PID 1. Non-interactive; -NoProfile keeps
# startup deterministic (no user profile evaluation).
ENTRYPOINT ["pwsh", "-NoProfile", "-NonInteractive", "-File", "/app/Listener.ps1"]
