/**
 * OutcomeCard - structured render for a side-effect completion
 *
 * spaarke-ai-architecture-redesign-r2 task 035 / FR-A1-06 (design D-F2). The client render
 * surface for the server's `OutcomeCard v1` contract
 * (`Services/Ai/PublicContracts/OutcomeCard.cs`). It UPGRADES the ad-hoc markdown
 * "[Open record](url)" link the gate-resolve path previously emitted into one structured card
 * carrying:
 *   - the user-facing summary (the audience split — never the model/internal detail),
 *   - a terminal status badge (Succeeded / Partial / Failed),
 *   - the SERVER-composed record/deep link as a clickable button (never a model-invented URL),
 *   - next-step chips derived from the Binding's DECLARED transitions,
 *   - a job-aware per-step progress strip when the completion is async (task 036 states).
 *
 * The component is presentational and context-agnostic (ADR-012): it never calls
 * Xrm.Navigation directly. Link/chip activation is delegated to optional callbacks; when a
 * callback is absent the link degrades to a normal new-tab anchor and chips render inert.
 *
 * @see ADR-021 - Fluent UI v9; design tokens; dark mode required
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Button, Badge } from '@fluentui/react-components';
import {
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  ClockRegular,
  OpenRegular,
  ArrowRightRegular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types — mirror OutcomeCard v1 (Services/Ai/PublicContracts/OutcomeCard.cs)
// Tolerant reader: unknown fields ignored; `status` accepts the server's camelCase
// enum string OR (defensively) a numeric enum ordinal.
// ─────────────────────────────────────────────────────────────────────────────

/** Terminal disposition of a side-effect outcome (mirrors server OutcomeStatus). */
export type OutcomeStatus = 'succeeded' | 'partial' | 'failed';

/** A server-composed record/deep link (mirrors server OutcomeCardLink). */
export interface IOutcomeCardLink {
  /** The navigable URL (server-composed — never model-invented). */
  url: string;
  /** Human-readable link label (e.g. "Open record"). */
  label: string;
  /** Link kind: `record | wizard | workspace | document`. */
  kind: string;
}

/** A declared next-step chip (mirrors server NextStepChip). */
export interface INextStepChip {
  /** Chip caption (e.g. "Analyze the document"). */
  label: string;
  /** What the chip triggers: `navigate | invoke_capability | dismiss`. */
  actionKind: string;
  /** Optional server-composed navigation target for a `navigate` chip. */
  targetUrl?: string | null;
}

/** One ordered step within a job-aware completion (mirrors server OutcomeStep). */
export interface IOutcomeStep {
  key: string;
  label: string;
  /** `pending | running | succeeded | failed`. */
  status: string;
}

/** Completion state hosted by the card (mirrors server OutcomeCompletion). */
export interface IOutcomeCompletion {
  /**
   * `singleShot | jobAware`. The BFF opts into string enums per-type (no global converter),
   * so this arrives as the camelCase string via the facade `JsonOptions` OR as a numeric
   * ordinal via an endpoint's default serializer — {@link isJobAwareMode} reads both.
   */
  mode: string | number;
  steps?: IOutcomeStep[];
}

/** The OutcomeCard v1 wire shape (mirrors server OutcomeCard). */
export interface IOutcomeCard {
  version?: number;
  ledgerOutputKey?: string;
  /** Server emits a camelCase enum string; a numeric ordinal is tolerated defensively. */
  status: OutcomeStatus | number | string;
  summary: {
    userFacing: string;
    /** Model/internal-facing detail — NEVER rendered to the user. */
    internal?: string | null;
  };
  link?: IOutcomeCardLink | null;
  nextSteps?: INextStepChip[];
  completion?: IOutcomeCompletion | null;
  traceRef?: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Status normalization
// ─────────────────────────────────────────────────────────────────────────────

/** Normalizes the wire `status` (string or numeric ordinal) to the display union. */
export function normalizeOutcomeStatus(status: OutcomeStatus | number | string): OutcomeStatus {
  if (typeof status === 'number') {
    // Server enum order: Succeeded=0, Partial=1, Failed=2.
    return status === 2 ? 'failed' : status === 1 ? 'partial' : 'succeeded';
  }
  const lower = String(status).toLowerCase();
  return lower === 'failed' ? 'failed' : lower === 'partial' ? 'partial' : 'succeeded';
}

/** True when the completion mode is job-aware — tolerant of the string OR numeric ordinal. */
export function isJobAwareMode(mode: string | number | undefined): boolean {
  // OutcomeCompletionMode: SingleShot=0, JobAware=1.
  return mode === 1 || String(mode).toLowerCase() === 'jobaware';
}

// ─────────────────────────────────────────────────────────────────────────────
// action_outcome SSE frame adapter (spaarke-ai-architecture-redesign-r2 task 044c)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Loosely-typed shape this adapter accepts — structurally matches the
 * `action_outcome` fields on `IChatSseEventData` (SprkChat/types.ts) without
 * importing that type here (keeps OutcomeCard.tsx free of a dependency on the
 * SSE event-envelope types, consistent with its existing presentational scope).
 */
export interface IActionOutcomeFrameData {
  status?: string | number;
  userSummary?: string;
  linkUrl?: string | null;
  linkLabel?: string | null;
  nextSteps?: string[];
  ledgerOutputKey?: string;
}

/**
 * Adapts the `action_outcome` SSE frame payload (mirrors server
 * `ChatSseActionOutcomeData` — `Api/Ai/ChatEndpoints.cs`) into the {@link IOutcomeCard}
 * shape this component renders.
 *
 * task 044c: the AUTO-EXECUTE (no-dialog) gate leg (`SideEffectGateAIFunction.cs`
 * ~line 490) emits `OutcomeCard.Render()`'s FLATTENED view-projection
 * (`OutcomeCardView` — labels only, no link `kind`, no chip `actionKind`) rather
 * than the full `OutcomeCard` v1 shape the gate-RESUME leg carries
 * (`dispatchConfirmedAction` / `GateResolveResult.Outcome`, consumed at
 * SprkChat.tsx ~761). This adapter restores the presentational defaults the
 * view-projection dropped, so BOTH legs render through the ONE `OutcomeCard`
 * component:
 *   - `link.kind`: `'record'` — the only link kind this leg composes (the Undo
 *     target for a reversible create, or the review-and-send record for an
 *     email draft; see `SideEffectGateAIFunction.BuildRecordUrl`).
 *   - `nextSteps[].actionKind`: `'invoke_capability'` — the only chip kind this
 *     leg declares today (the "Undo" chip; see `SideEffectGateAIFunction`'s
 *     `chips.Add(new NextStepChip("Undo", "invoke_capability"))`).
 *
 * Tolerant reader (035's convention): `status` accepts the server's lowercased
 * string ("succeeded" | "partial" | "failed") or, defensively, a differently-cased
 * string or numeric ordinal — {@link normalizeOutcomeStatus} does the actual
 * normalization at render time, so this adapter passes the raw value through.
 */
export function actionOutcomeDataToCard(data: IActionOutcomeFrameData): IOutcomeCard {
  return {
    ledgerOutputKey: data.ledgerOutputKey,
    status: data.status ?? 'succeeded',
    summary: { userFacing: data.userSummary ?? '' },
    link:
      typeof data.linkUrl === 'string' && data.linkUrl.length > 0
        ? { url: data.linkUrl, label: data.linkLabel || 'Open record', kind: 'record' }
        : undefined,
    nextSteps: (data.nextSteps ?? []).map(label => ({ label, actionKind: 'invoke_capability' })),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalXS,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  summary: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  successIcon: { color: tokens.colorPaletteGreenForeground1, fontSize: '20px' },
  partialIcon: { color: tokens.colorPaletteYellowForeground1, fontSize: '20px' },
  failureIcon: { color: tokens.colorPaletteRedForeground1, fontSize: '20px' },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    alignItems: 'center',
  },
  chips: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
  steps: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    marginTop: tokens.spacingVerticalXXS,
  },
  step: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/** Props for {@link OutcomeCard}. */
export interface IOutcomeCardProps {
  /** The OutcomeCard v1 payload from the server. */
  card: IOutcomeCard;
  /**
   * Called when the user activates the server-composed link. When omitted the link opens
   * in a new browser tab (the presentational fallback — the component never navigates via Xrm).
   */
  onOpenLink?: (link: IOutcomeCardLink) => void;
  /**
   * Called when the user activates a next-step chip. When omitted, chips render inert
   * (defensive UX — a chip with no host handler cannot act).
   */
  onNextStep?: (chip: INextStepChip) => void;
}

const STATUS_META: Record<
  OutcomeStatus,
  { label: string; color: 'success' | 'warning' | 'danger'; iconClass: keyof ReturnType<typeof useStyles> }
> = {
  succeeded: { label: 'Completed', color: 'success', iconClass: 'successIcon' },
  partial: { label: 'In progress', color: 'warning', iconClass: 'partialIcon' },
  failed: { label: 'Failed', color: 'danger', iconClass: 'failureIcon' },
};

function stepIcon(status: string): React.ReactElement {
  const s = status.toLowerCase();
  if (s === 'succeeded') return <CheckmarkCircleRegular />;
  if (s === 'failed') return <ErrorCircleRegular />;
  return <ClockRegular />;
}

/**
 * Renders a structured side-effect completion card.
 *
 * @example
 * ```tsx
 * <OutcomeCard
 *   card={{ status: 'succeeded', summary: { userFacing: 'Task created.' },
 *           link: { url: '…', label: 'Open record', kind: 'record' },
 *           nextSteps: [{ label: 'Assign it', actionKind: 'invoke_capability' }] }}
 *   onNextStep={(chip) => runChip(chip)}
 * />
 * ```
 */
export const OutcomeCard: React.FC<IOutcomeCardProps> = ({ card, onOpenLink, onNextStep }) => {
  const styles = useStyles();
  const status = normalizeOutcomeStatus(card.status);
  const meta = STATUS_META[status];

  const icon =
    status === 'succeeded'
      ? React.createElement(CheckmarkCircleRegular, { className: styles[meta.iconClass] as string })
      : status === 'partial'
        ? React.createElement(ClockRegular, { className: styles[meta.iconClass] as string })
        : React.createElement(ErrorCircleRegular, { className: styles[meta.iconClass] as string });

  const link = card.link ?? undefined;
  const nextSteps = card.nextSteps ?? [];
  const steps = card.completion && isJobAwareMode(card.completion.mode) ? (card.completion.steps ?? []) : [];

  return (
    <div className={styles.root} data-testid="outcome-card">
      <div className={styles.header}>
        {icon}
        <Text className={styles.summary}>{card.summary.userFacing}</Text>
        <Badge appearance="tint" color={meta.color} size="small">
          {meta.label}
        </Badge>
      </div>

      {steps.length > 0 && (
        <div className={styles.steps} data-testid="outcome-card-steps">
          {steps.map(step => (
            <div key={step.key} className={styles.step}>
              {stepIcon(step.status)}
              <Text>{step.label}</Text>
            </div>
          ))}
        </div>
      )}

      {(link || nextSteps.length > 0) && (
        <div className={styles.actions}>
          {link && (
            <Button
              appearance="primary"
              size="small"
              icon={<OpenRegular />}
              onClick={() => {
                if (onOpenLink) {
                  onOpenLink(link);
                } else if (typeof window !== 'undefined') {
                  window.open(link.url, '_blank', 'noopener,noreferrer');
                }
              }}
            >
              {link.label}
            </Button>
          )}

          {nextSteps.length > 0 && (
            <div className={styles.chips} data-testid="outcome-card-chips">
              {nextSteps.map((chip, i) => (
                <Button
                  key={`${chip.label}-${i}`}
                  appearance="subtle"
                  size="small"
                  icon={<ArrowRightRegular />}
                  iconPosition="after"
                  disabled={!onNextStep}
                  onClick={onNextStep ? () => onNextStep(chip) : undefined}
                >
                  {chip.label}
                </Button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default OutcomeCard;
