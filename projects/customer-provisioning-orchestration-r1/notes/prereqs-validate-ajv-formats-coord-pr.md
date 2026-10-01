# Coord note — `provisioning-prereqs-validate` ajv step fails on every run

> **Author**: customer-provisioning-orchestration-r1 T237 (2026-09-30)
> **Target owner**: `ci-cd-unit-test-remediation-r1` (owns `.github/workflows/**` since it reactivated 2026-08-27 —
> see `projects/INDEX.md`). This project does **not** edit the workflow.
> **Status**: ✅ **APPLIED 2026-09-30** by this project on the owner's instruction ("if there is a fix required and you
> can make it then please address it, do not defer"). No open PR or master change touched the workflow at the time.
> The skill's batch validation got the same fix (below).

## What fails

`.github/workflows/provisioning-prereqs-validate.yml`, step "Validate intake.schema.json is a valid JSON Schema":

```bash
npx --yes ajv-cli@5 compile --spec=draft2020 -s scripts/provisioning-prereqs/intake.schema.json
```

exits **1**:

```
schema scripts/provisioning-prereqs/intake.schema.json is invalid
error: unknown format "uuid" ignored in schema at path "#/properties/tenantId"
```

Reproduced 2026-09-30 against both the committed schema (`HEAD`, before T237) and the T237 version — the cause is
`"format": "uuid"` on `tenantId` / `subscriptionId`, which ajv-cli rejects in strict mode unless a formats plugin is
loaded. The workflow runs on every PR and on master (no paths filter), so this step is red for everyone. It is not
caused by T237. (T226 fixed the workflow's other step, `validate.ps1`, which was also red.)

## Proposed fix (verified locally, exit 0)

```yaml
      - name: Validate intake.schema.json is a valid JSON Schema (Draft 2020-12)
        shell: bash
        run: |
          npx --yes -p ajv-cli@5 -p ajv-formats@3 ajv compile \
            --spec=draft2020 \
            -c ajv-formats \
            -s scripts/provisioning-prereqs/intake.schema.json
```

Loading `ajv-formats` (rather than `--strict=false`) keeps the `uuid` format meaningful — strict mode off would
silently ignore it.

## Also stale (docs owned with the workflows)

`docs/procedures/ci-cd-workflow.md` § "Customer Provisioning: `provision-customer.yml`" documents a workflow deleted in
`902bebc49c` (its `validate-inputs` job checked "lowercase alphanumeric, 3-10 chars"; the customerId standard is now
3-8, starting with a letter — T237).

## Related — also fixed 2026-09-30

The `/provision-environment` batch path validated intakes with `ajv validate --spec draft2020 --strict false`, which
silently skipped `format: uuid` on `tenantId` (the §4D I1 field). It now runs the same `npx -p ajv-cli@5 -p
ajv-formats@3 ajv validate --spec=draft2020 -c ajv-formats` as CI — no global install needed. Verified: a valid intake
passes (exit 0); `tenantId: "not-a-guid"` is rejected (exit 1), which the old invocation accepted.

The stale `ci-cd-workflow.md` section is NOT fixed here: that doc's whole workflow section is out of date (plan G24).
