/**
 * Executor Metadata — 33-entry catalog for the PlaybookBuilder Node Types panel.
 *
 * Source of truth: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs`
 *   `public enum ExecutorType { ... }`
 *
 * Each entry mirrors one ExecutorType member and is tagged with a tier (6 tiers)
 * derived from the integer prefix per R7 design.md §11 + spec FR-22:
 *
 *   0–9   → AI         (LLM / embedding / analysis)
 *   10–19 → Compute    (rule engine, calculation, transform)
 *   20–29 → Mutations  (CRUD-side-effects on Dataverse / messaging)
 *   30–39 → Control    (conditional / parallel / wait / start)
 *   40–49 → Delivery   (output rendering, composite delivery, index)
 *   50+   → Capability (notification, query, lookup, agent, grounding,
 *                       LiveFact, retrieval, sufficiency, decline, return,
 *                       sanitization, observation, validator, load/return)
 *
 * Descriptions are condensed from the server XML doc comments to ~1 line for
 * panel rendering. The Tooltip on each tile (NodePalette.tsx) surfaces a fuller
 * description if needed.
 *
 * R7 Wave 8 task 082 (FR-22). Task 083 wires the typed config-form renderer
 * which uses the BFF GET /api/ai/playbook-builder/executor-config-schemas
 * endpoint (task 033 ✅) for the dynamic per-executor config form schema —
 * orthogonal to this metadata which is panel-rendering only.
 *
 * When a new ExecutorType is added to the server enum, add a matching entry
 * here (and consider whether it deserves a richer description for makers).
 */

import type { PlaybookNodeType } from '../types/canvas';

// ---------------------------------------------------------------------------
// Tier Definitions
// ---------------------------------------------------------------------------

/**
 * Tier ID for executor categorization. Used as Accordion item key.
 * Order matters — Accordion renders tiers in this declared order
 * (AI first, then Compute, etc.). See R7 design.md §11.
 */
export type ExecutorTier =
  | 'AI'
  | 'Compute'
  | 'Mutations'
  | 'Control'
  | 'Delivery'
  | 'Capability';

/** Display label for each tier shown in the Accordion header. */
export const TIER_LABEL: Record<ExecutorTier, string> = {
  AI: 'AI (0–9)',
  Compute: 'Compute (10–19)',
  Mutations: 'Mutations (20–29)',
  Control: 'Control (30–39)',
  Delivery: 'Delivery (40–49)',
  Capability: 'Capability (50+)',
};

/** Tier order — drives Accordion rendering order. */
export const TIER_ORDER: ExecutorTier[] = [
  'AI',
  'Compute',
  'Mutations',
  'Control',
  'Delivery',
  'Capability',
];

/** Compute tier from the executor's integer value per the prefix rule. */
function tierFromValue(value: number): ExecutorTier {
  if (value < 10) return 'AI';
  if (value < 20) return 'Compute';
  if (value < 30) return 'Mutations';
  if (value < 40) return 'Control';
  if (value < 50) return 'Delivery';
  return 'Capability';
}

// ---------------------------------------------------------------------------
// Executor Metadata Interface
// ---------------------------------------------------------------------------

/**
 * Metadata for one executor entry in the Node Types panel.
 */
export interface ExecutorMetadata {
  /** Executor Type Choice value (sprk_executortype). Matches server ExecutorType enum value. */
  value: number;
  /** PascalCase name from server enum (used for diagnostics, telemetry). */
  name: string;
  /** Human-facing label shown on the palette tile. */
  label: string;
  /** Tier prefix string shown as a leading badge (e.g., "01", "21"). Zero-padded to 2 chars (3 for ≥100). */
  tierPrefix: string;
  /** Tier category. */
  tier: ExecutorTier;
  /** 1-line description shown beneath the label (truncated by CSS if too long). */
  description: string;
  /**
   * Canvas-side discriminator (drives React Flow nodeTypes registry + renderer).
   * 13 of the 33 executors map to existing PlaybookNodeType values; the rest map to
   * a generic 'aiAnalysis' fallback until task 088 widens PlaybookNodeType to 33 entries.
   *
   * Per task 080 audit + design.md §3: canvas PlaybookNodeType may legitimately bucket
   * multiple server ExecutorTypes under one renderer. The Choice value stored on the
   * node (executorType field) is the source of dispatch truth — `type` is renderer hint only.
   */
  canvasType: PlaybookNodeType;
}

/** Zero-pad an integer value to a 2-character (or 3-char for ≥100) tier prefix. */
function prefix(value: number): string {
  if (value >= 100) return String(value);
  return value.toString().padStart(2, '0');
}

// ---------------------------------------------------------------------------
// EXECUTOR_METADATA — 33 entries (mirrors server ExecutorType enum)
// ---------------------------------------------------------------------------

/**
 * The 33-executor catalog, ordered by `value` ascending.
 *
 * To add a new executor: append the entry here AND on the server in `INodeExecutor.cs`
 * ExecutorType enum. Keep the ordering in sync with server values to make diffs predictable.
 */
export const EXECUTOR_METADATA: ExecutorMetadata[] = [
  // ───────────────────────────────  AI  (0–9)  ───────────────────────────────
  {
    value: 0,
    name: 'AiAnalysis',
    label: 'AI Analysis',
    tierPrefix: prefix(0),
    tier: tierFromValue(0),
    description: 'Run AI analysis using tool handlers (existing pipeline).',
    canvasType: 'aiAnalysis',
  },
  {
    value: 1,
    name: 'AiCompletion',
    label: 'AI Completion',
    tierPrefix: prefix(1),
    tier: tierFromValue(1),
    description: 'Raw LLM completion with a prompt template + structured-output schema.',
    canvasType: 'aiCompletion',
  },
  {
    value: 2,
    name: 'AiEmbedding',
    label: 'AI Embedding',
    tierPrefix: prefix(2),
    tier: tierFromValue(2),
    description: 'Generate embedding vectors for text (for RAG / similarity).',
    canvasType: 'aiAnalysis',
  },

  // ──────────────────────────  Compute  (10–19)  ──────────────────────────
  {
    value: 10,
    name: 'RuleEngine',
    label: 'Rule Engine',
    tierPrefix: prefix(10),
    tier: tierFromValue(10),
    description: 'Evaluate business rules over the current scope.',
    canvasType: 'aiAnalysis',
  },
  {
    value: 11,
    name: 'Calculation',
    label: 'Calculation',
    tierPrefix: prefix(11),
    tier: tierFromValue(11),
    description: 'Run a formula / computation against upstream values.',
    canvasType: 'aiAnalysis',
  },
  {
    value: 12,
    name: 'DataTransform',
    label: 'Data Transform',
    tierPrefix: prefix(12),
    tier: tierFromValue(12),
    description: 'Apply a JSON / XML transformation to upstream output.',
    canvasType: 'aiAnalysis',
  },

  // ────────────────────────  Mutations  (20–29)  ────────────────────────
  {
    value: 20,
    name: 'CreateTask',
    label: 'Create Task',
    tierPrefix: prefix(20),
    tier: tierFromValue(20),
    description: 'Create a Dataverse task record.',
    canvasType: 'createTask',
  },
  {
    value: 21,
    name: 'SendEmail',
    label: 'Send Email',
    tierPrefix: prefix(21),
    tier: tierFromValue(21),
    description: 'Send an email via Microsoft Graph with template-variable support.',
    canvasType: 'sendEmail',
  },
  {
    value: 22,
    name: 'UpdateRecord',
    label: 'Update Record',
    tierPrefix: prefix(22),
    tier: tierFromValue(22),
    description: 'Update fields on a Dataverse record.',
    canvasType: 'updateRecord',
  },
  {
    value: 23,
    name: 'CallWebhook',
    label: 'Call Webhook',
    tierPrefix: prefix(23),
    tier: tierFromValue(23),
    description: 'Call an external HTTP webhook with the upstream payload.',
    canvasType: 'aiAnalysis',
  },
  {
    value: 24,
    name: 'SendTeamsMessage',
    label: 'Send Teams Message',
    tierPrefix: prefix(24),
    tier: tierFromValue(24),
    description: 'Post a notification message to Microsoft Teams.',
    canvasType: 'sendEmail',
  },

  // ─────────────────────────  Control  (30–39)  ─────────────────────────
  {
    value: 30,
    name: 'Condition',
    label: 'Condition',
    tierPrefix: prefix(30),
    tier: tierFromValue(30),
    description: 'Branch downstream execution based on a conditional expression.',
    canvasType: 'condition',
  },
  {
    value: 31,
    name: 'Parallel',
    label: 'Parallel',
    tierPrefix: prefix(31),
    tier: tierFromValue(31),
    description: 'Fork execution into parallel branches that all advance simultaneously.',
    canvasType: 'condition',
  },
  {
    value: 32,
    name: 'Wait',
    label: 'Wait',
    tierPrefix: prefix(32),
    tier: tierFromValue(32),
    description: 'Pause for a duration, until a datetime, or for human approval.',
    canvasType: 'wait',
  },
  {
    value: 33,
    name: 'Start',
    label: 'Start',
    tierPrefix: prefix(33),
    tier: tierFromValue(33),
    description: 'Canvas anchor node — pass-through with no execution logic.',
    canvasType: 'start',
  },

  // ────────────────────────  Delivery  (40–49)  ────────────────────────
  {
    value: 40,
    name: 'DeliverOutput',
    label: 'Deliver Output',
    tierPrefix: prefix(40),
    tier: tierFromValue(40),
    description: 'Render and deliver the final output (markdown, HTML, text, JSON).',
    canvasType: 'deliverOutput',
  },
  {
    value: 41,
    name: 'DeliverToIndex',
    label: 'Deliver to Index',
    tierPrefix: prefix(41),
    tier: tierFromValue(41),
    description: 'Queue a document for RAG semantic indexing.',
    canvasType: 'deliverToIndex',
  },
  {
    value: 42,
    name: 'DeliverComposite',
    label: 'Deliver Composite',
    tierPrefix: prefix(42),
    tier: tierFromValue(42),
    description: 'Compose N upstream Action outputs by sectionName into a single composite.',
    canvasType: 'deliverOutput',
  },

  // ───────────────────────  Capability  (50+)  ───────────────────────
  {
    value: 50,
    name: 'CreateNotification',
    label: 'Create Notification',
    tierPrefix: prefix(50),
    tier: tierFromValue(50),
    description: 'Create an in-app notification for a user via appnotification.',
    canvasType: 'createNotification',
  },
  {
    value: 51,
    name: 'QueryDataverse',
    label: 'Query Dataverse',
    tierPrefix: prefix(51),
    tier: tierFromValue(51),
    description: 'Execute a FetchXML query against Dataverse and return results.',
    canvasType: 'aiAnalysis',
  },
  {
    value: 52,
    name: 'LookupUserMembership',
    label: 'Lookup User Membership',
    tierPrefix: prefix(52),
    tier: tierFromValue(52),
    description: 'Resolve the caller’s record memberships for a given entity type.',
    canvasType: 'lookupUserMembership',
  },
  {
    value: 60,
    name: 'AgentService',
    label: 'Agent Service',
    tierPrefix: prefix(60),
    tier: tierFromValue(60),
    description: 'Route the node to Azure AI Foundry Agent Service (Phase 2).',
    canvasType: 'aiAnalysis',
  },
  {
    value: 70,
    name: 'GroundingVerify',
    label: 'Grounding Verify',
    tierPrefix: prefix(70),
    tier: tierFromValue(70),
    description: 'Zero-LLM citation check — verify quoted evidence matches source chunks.',
    canvasType: 'entityNameValidator',
  },
  {
    value: 80,
    name: 'LiveFact',
    label: 'Live Fact',
    tierPrefix: prefix(80),
    tier: tierFromValue(80),
    description: 'Resolve a deterministic Live Fact about a Dataverse subject (confidence 1.0).',
    canvasType: 'aiAnalysis',
  },
  {
    value: 90,
    name: 'IndexRetrieve',
    label: 'Index Retrieve',
    tierPrefix: prefix(90),
    tier: tierFromValue(90),
    description: 'Filter + vector search over spaarke-insights-index — Observations + Precedents.',
    canvasType: 'aiAnalysis',
  },
  {
    value: 100,
    name: 'EvidenceSufficiency',
    label: 'Evidence Sufficiency',
    tierPrefix: prefix(100),
    tier: tierFromValue(100),
    description: 'Apply a configured evidence rule; emit sufficient/insufficient verdict + gap analysis.',
    canvasType: 'condition',
  },
  {
    value: 110,
    name: 'DeclineToFind',
    label: 'Decline to Find',
    tierPrefix: prefix(110),
    tier: tierFromValue(110),
    description: 'Deterministic exit — emit a structured DeclineResponse when evidence is insufficient.',
    canvasType: 'deliverOutput',
  },
  {
    value: 120,
    name: 'ReturnInsightArtifact',
    label: 'Return Insight Artifact',
    tierPrefix: prefix(120),
    tier: tierFromValue(120),
    description: 'Terminal node — serialize upstream outputs into an InsightArtifact envelope.',
    canvasType: 'deliverOutput',
  },
  {
    value: 130,
    name: 'Sanitization',
    label: 'Sanitization',
    tierPrefix: prefix(130),
    tier: tierFromValue(130),
    description: 'Sanitize raw document text for LLM consumption — strip control chars + retrieval noise.',
    canvasType: 'entityNameValidator',
  },
  {
    value: 140,
    name: 'ObservationEmit',
    label: 'Observation Emit',
    tierPrefix: prefix(140),
    tier: tierFromValue(140),
    description: 'Emit N observations (one per surviving L2 candidate) + the L1 classification.',
    canvasType: 'deliverOutput',
  },
  {
    value: 141,
    name: 'EntityNameValidator',
    label: 'Entity Name Validator',
    tierPrefix: prefix(141),
    tier: tierFromValue(141),
    description: 'Scrub LLM-emitted entity names against an allow-list; log hallucinations.',
    canvasType: 'entityNameValidator',
  },
  {
    value: 142,
    name: 'LoadKnowledge',
    label: 'Load Knowledge',
    tierPrefix: prefix(142),
    tier: tierFromValue(142),
    description: 'Canvas-only Control — evaluate passthroughBinding templates + bind to OutputVariable.',
    canvasType: 'condition',
  },
  {
    value: 143,
    name: 'ReturnResponse',
    label: 'Return Response',
    tierPrefix: prefix(143),
    tier: tierFromValue(143),
    description: 'Terminal Control — bind responseBinding map to the playbook’s return value.',
    canvasType: 'deliverOutput',
  },
];

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Group EXECUTOR_METADATA by tier (preserves the ordering of entries within each tier).
 * Returns a map from tier → entries, in TIER_ORDER.
 */
export function groupExecutorsByTier(): Record<ExecutorTier, ExecutorMetadata[]> {
  const grouped: Record<ExecutorTier, ExecutorMetadata[]> = {
    AI: [],
    Compute: [],
    Mutations: [],
    Control: [],
    Delivery: [],
    Capability: [],
  };
  for (const entry of EXECUTOR_METADATA) {
    grouped[entry.tier].push(entry);
  }
  return grouped;
}

/**
 * Look up an executor by its Choice value. Returns undefined if no match.
 */
export function getExecutorByValue(value: number): ExecutorMetadata | undefined {
  return EXECUTOR_METADATA.find(e => e.value === value);
}

/**
 * Look up an executor by its enum name (case-sensitive PascalCase).
 */
export function getExecutorByName(name: string): ExecutorMetadata | undefined {
  return EXECUTOR_METADATA.find(e => e.name === name);
}

/** Total entry count — useful as a build-time assertion. */
export const EXECUTOR_METADATA_COUNT = EXECUTOR_METADATA.length; // 33
