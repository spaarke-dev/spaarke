# -----------------------------------------------------------------------------
# Sprk.Intake.EngineJob — the NDA redline engines as an ADR-004 Amendment A2
# queue consumer (spaarke-intake-pipeline-r1 task 001, FR-01; design §5).
#
# Runtime model:
#   ONE event-triggered Container Apps Job (infrastructure/bicep/modules/
#   intake-engine-job.bicep, task 002). KEDA starts an execution per message on
#   intake-engine-requests; the execution receives ONE message, runs the
#   accelerator's engines on blob-staged inputs, writes outputs to deterministic
#   staging paths, sends a completion to intake-engine-completions and exits.
#   replicaTimeout (600 s) is the hard stop; Service Bus redelivery is the retry.
#   The job runs as its OWN user-assigned managed identity (AZURE_CLIENT_ID):
#   two queues, the staging container, AcrPull. No BFF calls, no secrets.
#
# What is in the image:
#   - Python 3.11 (base pinned by digest below).
#   - The accelerator's engines, COPYed UNCHANGED from src/accelerators/core/
#     engines/ at build time (never vendored into this folder; FR-01). They are
#     stdlib + lxml only and run as child processes.
#   - The NDA practice's summary intro (summary_table.py --intro).
#   - The entrypoint package (entrypoint/*.py) and its hash-pinned dependencies
#     from requirements.txt, installed with --require-hashes, binary wheels only:
#     lxml for the engines; azure-identity, azure-servicebus, azure-storage-blob
#     and the azure-monitor-opentelemetry distro for the entrypoint layer only.
#
# Attack surface = CPython + those wheels. The job parses UNTRUSTED .docx from
# any intake sender, so: non-root user, no shell entrypoint, engine children
# get an environment with the identity endpoint/header and telemetry settings
# removed (entrypoint/engines.py), and the build context is an allowlist
# (Dockerfile.dockerignore).
# Deliberately absent: pip/setuptools/wheel at runtime (removed after install),
# a compiler, the engines' tests, the accelerator's corpus and answer-key, any
# BFF code (ADR-052: never a reference to Sprk.Bff.Api), curl/wget.
#
# Size: ceiling 400 MB compressed, enforced by the CI size step
# (.github/workflows/build-intake-engine-image.yml, MAX_COMPRESSED_MB). It is a
# runaway guard; tighten it from the first measured size.
# Scan posture: Trivy gate on fixable HIGH/CRITICAL in the OS layer AND the
# Python packages, in CI before any push (task 003).
#
# Build context: the REPO ROOT (the engines live outside this folder):
#   docker build -f src/server/services/Sprk.Intake.EngineJob/Dockerfile .
# -----------------------------------------------------------------------------

# python 3.11.18, Debian 13 (trixie) slim; multi-arch index digest, verified 2026-11-26. Bump the tag AND the
# digest together, never the tag alone.
FROM python:3.11.18-slim-trixie@sha256:61e01f72680f86a5279ad510818ab7b2ee9f049822589d4e34e33e044360356c

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    HOME=/tmp

WORKDIR /app

# Debian security fixes published after the base digest was cut (e.g. libzstd1 CVE-2026-124851, fixed in
# 1.5.7+dfsg-1+deb13u1, 2026-11-26). Upgrade-only: no new packages, the apt lists are removed in the same layer.
# The Trivy gate still fails the build on anything fixable that this does not pick up. Bumping the base digest stays
# the main way to move.
RUN apt-get update \
    && apt-get -y --no-install-recommends upgrade \
    && rm -rf /var/lib/apt/lists/*

# Dependencies first (cache layer). --require-hashes: every package, transitive ones included, must match a hash in
# requirements.txt. --only-binary: no sdist builds (the slim base has no compiler, and a build step is code we did
# not pin). Then pip itself goes: nothing at runtime installs packages.
COPY src/server/services/Sprk.Intake.EngineJob/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir --require-hashes --only-binary=:all: -r /app/requirements.txt \
    && pip uninstall -y pip setuptools wheel \
    && rm /app/requirements.txt

# The engines, unchanged, and the NDA summary intro they take.
COPY src/accelerators/core/engines/*.py /app/engines/
COPY src/accelerators/practices/nda/skills/nda-review/assets/summary-intro.md /app/assets/summary-intro.md

# The entrypoint layer.
COPY src/server/services/Sprk.Intake.EngineJob/entrypoint/*.py /app/entrypoint/

# -----------------------------------------------------------------------------
# BUILD-TIME CONTRACT CHECK: a broken image fails the build, not a live run.
#   - Python is 3.11; lxml and the entrypoint's SDKs import.
#   - The four engines the job runs are present and start (--help exits 0).
#   - No engine module imports an Azure SDK (they stay stdlib + lxml).
# -----------------------------------------------------------------------------
RUN python -c "import sys; assert sys.version_info[:2] == (3, 11), sys.version" \
    && python -c "import lxml.etree, azure.identity, azure.servicebus, azure.storage.blob, azure.monitor.opentelemetry, entrypoint.main" \
    && for e in segment_clauses ooxml_redline summary_table check_outputs; do \
         python -I /app/engines/$e.py --help > /dev/null || exit 1; \
       done \
    && ! grep -lE '^[[:space:]]*(import|from)[[:space:]]+azure' /app/engines/*.py

# Non-root (no passwd entry needed: the job reads no home directory; HOME=/tmp above).
USER 10001:10001

ENTRYPOINT ["python", "-m", "entrypoint.main"]
