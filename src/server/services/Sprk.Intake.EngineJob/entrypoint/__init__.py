"""Intake engine job: the ADR-004 Amendment A2 queue consumer that runs the accelerator's NDA engines.

Modules:
  envelope  - the Job Contract envelope, the payload contract and the deterministic staging paths
  engines   - runs the accelerator engines (unchanged, as subprocesses) and maps their exit codes to outcomes
  azure_io  - Service Bus and Blob access over the pinned Azure SDKs, with the job's managed identity
  main      - one execution = receive one message, process it, settle it, exit
"""
