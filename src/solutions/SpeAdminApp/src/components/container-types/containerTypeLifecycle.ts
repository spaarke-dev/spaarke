/**
 * Container-type lifecycle constraints — the sourced facts behind the creation flow's warnings.
 *
 * Every statement here is quoted or paraphrased from Microsoft's own container-type documentation,
 * captured in this repo at `knowledge/sharepoint-embedded/docs/learn-containertypes.md` (fetched
 * 2026-05-14 from
 * https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/containertypes).
 * Line references below point into that file so a reader can check any claim without leaving the repo.
 *
 * WHY THIS FILE EXISTS (spec FR-C13 / task 030). Creating a container type is close to irreversible:
 * the billing classification can never be changed, the owning app is bound 1:1 and permanently, and a
 * non-trial container type **cannot be deleted at all**. None of that was stated anywhere in the UI —
 * an admin discovered it by failing, or worse, never discovered it (a trial type simply stops working
 * on day 31). Keeping the facts as data rather than as JSX prose means they can be asserted in tests
 * and re-checked against the source when the corpus is refreshed (task 061).
 *
 * ADR-021: no presentation here — this module is pure data. Rendering lives in the dialog.
 */

/** The three billing classifications Graph accepts on create. */
export type BillingClassificationValue = "trial" | "standard" | "directToCustomer";

/** How permanent a consequence is — drives how loudly the UI states it. */
export type ConsequenceSeverity = "irreversible" | "limit" | "obligation";

export interface LifecycleConsequence {
  /** Short label shown in the consequences list. */
  readonly text: string;
  readonly severity: ConsequenceSeverity;
}

export interface BillingClassificationProfile {
  readonly value: BillingClassificationValue;
  readonly label: string;
  /** One line describing what this classification is for. */
  readonly summary: string;
  /** Whether a container type of this classification can ever be deleted. */
  readonly deletable: boolean;
  /** Whether it counts against the 25-per-tenant ceiling for production types. */
  readonly countsTowardProductionLimit: boolean;
  /** Consequences the admin must see BEFORE submit. */
  readonly consequences: readonly LifecycleConsequence[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Documented ceilings
// ─────────────────────────────────────────────────────────────────────────────

/**
 * "Each tenant can have 25 container types at a time." — learn-containertypes.md:75.
 *
 * Note the sentence sits under the "Standard container types (nontrial)" heading, so it reads as the
 * production ceiling. We surface it as a stated limit rather than computing "N remaining", because the
 * count we can observe is not guaranteed to be tenant-complete — see `notes/task-030-findings.md`.
 */
export const PRODUCTION_CONTAINER_TYPE_LIMIT = 25;

/**
 * "Each developer can have only one container type with `trial` billing classification in their
 * tenant at a time." — learn-containertypes.md:61.
 */
export const TRIAL_CONTAINER_TYPE_LIMIT = 1;

/** "The container type expires after 30 days." — learn-containertypes.md:69. */
export const TRIAL_VALIDITY_DAYS = 30;

// ─────────────────────────────────────────────────────────────────────────────
// Constraints that hold for every classification
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Constraints independent of the billing choice. Stated on every path through the creation flow.
 */
export const UNIVERSAL_CONSEQUENCES: readonly LifecycleConsequence[] = [
  {
    // learn-containertypes.md:11 + :95 — "SharePoint Embedded mandates a 1:1 relationship between the
    // owning application and a container type" / "A single owning app can only own one container type
    // at a time."
    text:
      "The owning application is bound to this container type 1:1 and permanently. That application " +
      "cannot own any other container type, and this container type cannot be moved to another " +
      "application.",
    severity: "irreversible",
  },
  {
    // learn-containertypes.md:13 — ContainerTypeID is an immutable property.
    text: "The container type ID is assigned by SharePoint Embedded and can never be changed.",
    severity: "irreversible",
  },
  {
    // learn-containertypes.md:21 — "A container type set for trial purposes can't be converted for
    // production; or vice versa."
    text:
      "The billing classification is fixed at creation. A trial container type can never be converted " +
      "to production, and a production container type can never be converted to trial.",
    severity: "irreversible",
  },
  {
    // learn-containertypes.md:22 — conversion is blocked in BOTH directions.
    text:
      "Standard and pass-through billing cannot be converted into one another in either direction. " +
      "Changing between them means deleting and re-creating the container type.",
    severity: "irreversible",
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Per-classification profiles
// ─────────────────────────────────────────────────────────────────────────────

export const BILLING_CLASSIFICATION_PROFILES: readonly BillingClassificationProfile[] = [
  {
    value: "trial",
    label: "Trial",
    summary: "For evaluation and development in this tenant only. Free, time-limited, and deletable.",
    deletable: true,
    countsTowardProductionLimit: false,
    consequences: [
      {
        // learn-containertypes.md:69
        text: `This container type expires ${TRIAL_VALIDITY_DAYS} days after creation. It is not renewable — after that it stops working.`,
        severity: "irreversible",
      },
      {
        // learn-containertypes.md:71 — the one most likely to be discovered by failing, because the
        // app offers a Register action that cannot succeed for a trial type.
        text:
          "A trial container type only works in this tenant. It cannot be registered on another " +
          "consuming tenant, so the Register action will not succeed for it.",
        severity: "limit",
      },
      {
        // learn-containertypes.md:67-68
        text: "Limited to 5 containers, each capped at 1 GB of storage.",
        severity: "limit",
      },
      {
        // learn-containertypes.md:61
        text: `Only ${TRIAL_CONTAINER_TYPE_LIMIT} trial container type may exist in a tenant at a time.`,
        severity: "limit",
      },
      {
        // learn-containertypes.md:70
        text:
          "To create a replacement trial later, every container of the existing trial type must first " +
          "be permanently deleted.",
        severity: "obligation",
      },
    ],
  },
  {
    value: "standard",
    label: "Standard",
    summary: "Production. Consumption is billed to this tenant, which must hold a valid Azure billing profile.",
    deletable: false,
    countsTowardProductionLimit: true,
    consequences: [
      {
        // learn-containertypes.md:109 — the single most consequential unstated fact in the flow.
        text:
          "A standard container type can never be deleted. Microsoft does not support deleting " +
          "non-trial container types, so a mistake here is permanent.",
        severity: "irreversible",
      },
      {
        // learn-containertypes.md:79 + :89-93 — and per this project's scope, Spaarke cannot do it.
        text:
          "Billing must be attached separately in PowerShell (Add-SPOContainerTypeBilling) against an " +
          "Azure subscription and resource group in this tenant. This app cannot attach it, and the " +
          "container type is not usable until an administrator does.",
        severity: "obligation",
      },
      {
        text: `Counts toward the limit of ${PRODUCTION_CONTAINER_TYPE_LIMIT} container types per tenant.`,
        severity: "limit",
      },
    ],
  },
  {
    value: "directToCustomer",
    label: "Direct to Customer (pass-through)",
    summary: "Production. Consumption is billed to each consuming tenant rather than to this one.",
    deletable: false,
    countsTowardProductionLimit: true,
    consequences: [
      {
        // learn-containertypes.md:109
        text:
          "A pass-through container type can never be deleted. Microsoft does not support deleting " +
          "non-trial container types, so a mistake here is permanent.",
        severity: "irreversible",
      },
      {
        // learn-containertypes.md:80
        text:
          "Consumption charges go to the consuming tenant, not this one. No Azure billing profile is " +
          "set up here — but each consuming tenant takes on the cost.",
        severity: "obligation",
      },
      {
        text: `Counts toward the limit of ${PRODUCTION_CONTAINER_TYPE_LIMIT} container types per tenant.`,
        severity: "limit",
      },
    ],
  },
];

/** Look up a profile by classification value. Falls back to trial, matching the dialog's default. */
export function profileFor(value: BillingClassificationValue): BillingClassificationProfile {
  return (
    BILLING_CLASSIFICATION_PROFILES.find((p) => p.value === value) ??
    BILLING_CLASSIFICATION_PROFILES[0]
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Quota evaluation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * What the visible container-type list lets us say about a limit — and, deliberately, what it does not.
 *
 * The list a caller receives is scoped to what that caller can see; tenant-wide visibility depends on
 * an Entra directory role the BFF cannot observe (proven in task 012 — the `wids` claim never reaches
 * the token). So an observed count is a **lower bound** on the tenant's true count, never the count
 * itself.
 *
 * That asymmetry is usable rather than fatal:
 *  - Seeing a trial container type PROVES one exists, so we can block a second one with certainty.
 *  - NOT seeing one proves nothing, so we must not claim the slot is free.
 *
 * Hence `atLimit` is only ever true on proof, and we never publish a "remaining" number. Reporting
 * "22 of 25 remaining" from a lower bound would be a guess dressed as a fact — the exact defect class
 * this project exists to remove.
 */
export interface QuotaAssessment {
  /** Container types of this classification visible to the caller. A lower bound, not a census. */
  readonly observedCount: number;
  /** The documented ceiling for this classification. */
  readonly limit: number;
  /** True only when the observed set alone proves the limit is already reached. */
  readonly atLimit: boolean;
  /** Explanation shown to the admin. Never asserts headroom the data cannot support. */
  readonly message: string;
}

/**
 * Assess the one-trial-per-tenant limit against the visible list.
 *
 * @param containerTypes Billing classifications of the container types currently visible.
 */
export function assessTrialQuota(
  containerTypes: readonly { billingClassification?: string | null }[]
): QuotaAssessment {
  const observedCount = containerTypes.filter(
    (ct) => (ct.billingClassification ?? "").toLowerCase() === "trial"
  ).length;

  const atLimit = observedCount >= TRIAL_CONTAINER_TYPE_LIMIT;

  return {
    observedCount,
    limit: TRIAL_CONTAINER_TYPE_LIMIT,
    atLimit,
    message: atLimit
      ? `This tenant already has a trial container type, and only ${TRIAL_CONTAINER_TYPE_LIMIT} is ` +
        "allowed at a time. Creating another will be rejected. Permanently delete the existing trial " +
        "type and all of its containers first."
      : `A tenant may hold ${TRIAL_CONTAINER_TYPE_LIMIT} trial container type at a time. No trial ` +
        "type is visible to you, but this list only covers what your account can see — if the tenant " +
        "already has one elsewhere, creation will be rejected.",
  };
}

/**
 * Describe the production ceiling. Deliberately reports the limit and the observed count separately
 * instead of subtracting them, because the count is a lower bound (see {@link QuotaAssessment}).
 */
export function describeProductionQuota(
  containerTypes: readonly { billingClassification?: string | null }[]
): QuotaAssessment {
  const observedCount = containerTypes.filter((ct) => {
    const classification = (ct.billingClassification ?? "").toLowerCase();
    return classification === "standard" || classification === "directtocustomer";
  }).length;

  return {
    observedCount,
    limit: PRODUCTION_CONTAINER_TYPE_LIMIT,
    // Never asserted from a lower bound — reaching the ceiling is reported by Graph, not guessed here.
    atLimit: false,
    message:
      `A tenant may hold ${PRODUCTION_CONTAINER_TYPE_LIMIT} container types at a time. ` +
      `${observedCount} production ${observedCount === 1 ? "type is" : "types are"} visible to you; ` +
      "the tenant total may be higher, because this list only covers what your account can see.",
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Replication + consuming-tenant overrides (task 026 / spec FR-C08)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * "Updating settings on a container type may take up to **24 hours** for the new values to be
 * replicated on all consuming tenants." — learn-containertypes.md:101.
 */
export const REPLICATION_MAX_HOURS = 24;

/**
 * What the UI may honestly say after a settings save.
 *
 * **There is no third "replicated" state, deliberately.** Graph exposes no replication signal of any
 * kind — `replicat*` does not appear anywhere in either the v1.0 or the beta CSDL (see
 * `notes/task-026-findings.md` §1). So nothing ever reports the pending → replicated transition, and
 * there is no honest moment at which the UI could flip.
 *
 * Flipping on a timer would be worse than the bare "Saved" this replaces: it would look authoritative
 * while being invented. NFR-06 forbids exactly that.
 */
export const SAVE_ACCEPTED_TITLE = "Saved — replication is pending";

/** Body of the post-save notice. Both sentences are sourced from learn-containertypes.md:101. */
export const SAVE_ACCEPTED_DETAIL =
  `The change was accepted. It may take up to ${REPLICATION_MAX_HOURS} hours to reach consuming ` +
  "tenants, and this API does not report when replication finishes — so this message will not " +
  "change to \"replicated\". Any setting a consuming tenant has already overridden keeps its " +
  "override and will not pick up the new value.";

/**
 * Parse `consumingTenantOverridables` — a comma-delimited flag string, NOT an array or a typed enum.
 *
 * **Unrecognised flags are preserved, never dropped.** Graph's own published enum is narrower than its
 * own responses: all four live Spaarke Dev container types return `sharingCapability` in this string,
 * and `sharingCapability` is not a member of `fileStorageContainerTypeSettingsOverride` in **either**
 * API version. Filtering to "known" members would silently discard a real, live flag — the failure
 * task 025 avoided in the SDK and this avoids again in the client.
 */
export function parseConsumingTenantOverridables(
  raw?: string | null
): readonly string[] {
  if (!raw) return [];
  return raw
    .split(",")
    .map((f) => f.trim())
    .filter((f) => f.length > 0 && f !== "unknownFutureValue");
}

/**
 * Whether a consuming tenant is PERMITTED to override this setting.
 *
 * Note carefully what this does and does not say. It reports a **permission**, not a **state** — the
 * setting may or may not actually be overridden anywhere. `consumingTenantOverridables` carries no
 * effective value and no indication that an override exists; the effective value lives on a
 * `fileStorageContainerTypeRegistration` in the CONSUMING tenant, which the owning tenant cannot read
 * (`notes/task-026-findings.md` §2).
 *
 * Presenting this as "overridden" would assert something the response never said.
 */
export function isOverridableByConsumingTenant(
  settingName: string,
  overridables?: string | null
): boolean {
  const flags = parseConsumingTenantOverridables(overridables);
  return flags.some((f) => f.toLowerCase() === settingName.toLowerCase());
}

/** Human label for a settings property name, for the overridable list. */
const SETTING_LABELS: Readonly<Record<string, string>> = {
  sharingCapability: "External sharing",
  isItemVersioningEnabled: "Item versioning",
  itemMajorVersionLimit: "Major version limit",
  maxStoragePerContainerInBytes: "Storage ceiling per container",
  isSearchEnabled: "Search indexing",
  isDiscoverabilityEnabled: "Discoverability",
  isSharingRestricted: "Sharing restriction",
  urlTemplate: "URL template",
  isOfficeRestricted: "Office restriction",
};

/**
 * Label an overridable flag for display, falling back to the raw flag name.
 *
 * The fallback matters: a flag Graph adds later, or one already live but absent from the published
 * enum, still renders as itself rather than vanishing.
 */
export function labelForSetting(settingName: string): string {
  return SETTING_LABELS[settingName] ?? settingName;
}

// ─────────────────────────────────────────────────────────────────────────────
// Billing standing (task 029 / spec FR-C12)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Billing standing as the UI must model it: Graph's two real values plus an explicit UNKNOWN.
 *
 * Three states, not two. Graph's enum is `invalid | valid | unknownFutureValue`, but the field can
 * also be absent — and absent is not valid. Collapsing "not reported" into either real value is the
 * defect NFR-06 names, and for billing it is the expensive direction: a container type whose billing
 * has lapsed would read as healthy.
 */
export type BillingStanding = "valid" | "invalid" | "unknown";

export interface BillingAssessment {
  readonly standing: BillingStanding;
  /** Badge label. Never blank — an empty badge reads as a state rather than as absence. */
  readonly label: string;
  /** Fluent Badge colour for this standing. */
  readonly tone: "success" | "danger" | "informative";
  /** True only for a reported `invalid`. Absence is not an alarm, and must not raise one. */
  readonly needsAttention: boolean;
  /** What this standing means operationally, or null when there is nothing to say. */
  readonly consequence: string | null;
  /**
   * Where it is remediated, or null when this app cannot honestly route the admin anywhere.
   * A null here is deliberate: inventing a remediation is worse than admitting the docs are silent.
   */
  readonly remediation: string | null;
}

/**
 * Normalise a raw wire value to a {@link BillingStanding}.
 *
 * Anything unrecognised — including Graph's `unknownFutureValue` sentinel and any member added after
 * this was written — becomes `unknown` rather than being coerced into `valid`.
 */
export function toBillingStanding(raw?: string | null): BillingStanding {
  const v = (raw ?? "").trim().toLowerCase();
  if (v === "valid") return "valid";
  if (v === "invalid") return "invalid";
  return "unknown";
}

/**
 * Assess a container type's billing standing, and say what an `invalid` means *for this
 * classification*.
 *
 * The classification split is load-bearing and is the part FR-C12 does not mention. Only a
 * `standard` container type requires a billing profile in the developer tenant
 * (learn-containertypes.md:79). A `directToCustomer` type bills the consuming tenant and the
 * developer tenant "doesn't need to set up an Azure billing profile" (:80), and a `trial` type
 * "isn't linked to any Azure billing profile" at all (:61). So a single generic "attach a billing
 * profile" warning would instruct an admin to do something that is wrong for two of the three
 * classifications.
 */
export function assessBilling(ct: {
  billingStatus?: string | null;
  billingClassification?: string | null;
}): BillingAssessment {
  const standing = toBillingStanding(ct.billingStatus);
  const classification = (ct.billingClassification ?? "").trim().toLowerCase();

  if (standing === "valid") {
    return {
      standing,
      label: "Valid",
      tone: "success",
      needsAttention: false,
      consequence: null,
      remediation: null,
    };
  }

  if (standing === "unknown") {
    return {
      standing,
      label: "Unknown",
      tone: "informative",
      needsAttention: false,
      consequence:
        "Microsoft Graph did not report a billing status for this container type. This is not a " +
        "statement that billing is valid — it means the value was not returned.",
      remediation: null,
    };
  }

  // standing === "invalid" — what that means depends entirely on the classification.
  if (classification === "standard") {
    return {
      standing,
      label: "Invalid",
      tone: "danger",
      needsAttention: true,
      consequence:
        "Consumption charges for a standard container type are billed to this tenant, and a standard " +
        "type requires a valid Azure billing profile. While billing is invalid, the container type is " +
        "not fully provisioned for billable production use.",
      remediation:
        "A Global Administrator in this (developer) tenant attaches a billing profile with " +
        "Add-SPOContainerTypeBilling in the SharePoint Online Management Shell, using an Azure " +
        "subscription and resource group in this tenant. SPE Admin does not perform this — it is a " +
        "provisioning step and needs Azure subscription rights this app does not hold.",
    };
  }

  if (classification === "directtocustomer") {
    return {
      standing,
      label: "Invalid",
      tone: "danger",
      needsAttention: true,
      consequence:
        "This is a passthrough (directToCustomer) container type, so consumption is billed to the " +
        "consuming tenant rather than to this one, and this tenant does not attach a billing profile " +
        "for it.",
      remediation:
        "Attaching a billing profile here would be the wrong fix — that applies to standard types " +
        "only. Microsoft's container-type documentation does not state how billing becomes invalid " +
        "for a passthrough type, so check the consuming tenant's registration and its own Azure " +
        "billing setup before changing anything.",
    };
  }

  if (classification === "trial") {
    return {
      standing,
      label: "Invalid",
      tone: "danger",
      needsAttention: true,
      consequence:
        "A trial container type is not linked to an Azure billing profile at all, so an invalid " +
        "billing status is unexpected for one. Note that a trial type expires 30 days after creation " +
        "regardless of billing.",
      remediation:
        "There is no billing profile to repair on a trial type. If this type is being used beyond " +
        "development, it needs replacing with a standard or passthrough type — classification cannot " +
        "be changed after creation.",
    };
  }

  // Classification unknown: report the status truthfully and do not guess a remediation.
  return {
    standing,
    label: "Invalid",
    tone: "danger",
    needsAttention: true,
    consequence:
      "Microsoft Graph reports this container type's billing as invalid. Its billing classification " +
      "was not reported, and the remediation depends on that classification.",
    remediation:
      "Determine the billing classification first: a standard type needs a billing profile attached " +
      "in this tenant, while a passthrough type is billed to the consuming tenant and is repaired " +
      "there.",
  };
}
