/**
 * BillingGuidelinesWidget — the outside-counsel "Billing Guidelines" view (ebilling-r1 task 135, FR-49, D-66).
 *
 * Shows the published billing rules in force today for the caller's firm(s), grouped by rule set, each set with its scope, version and
 * dates and each rule with a plain-language description, its parameters and the contract clause as the firm may see it. Over
 * `GET /api/v1/external/ebilling/billing-guidelines` (see `api/billing-guidelines-client.ts`). The client shows what the server returns
 * and decides nothing about who may see what (NFR-06).
 *
 * STATES:
 *   - loading: a spinner.
 *   - ready, no sets: an empty state ("no billing guidelines have been shared with you").
 *   - ready: one heading per firm when the caller has several, then a section per set. A rule the BFF could not describe is listed
 *     with its name and clause and a marker line; the view never writes a description of its own.
 *   - 503 `ebilling.disabled` (the kill switch is off in this environment): the view is hidden. The body renders nothing, no message
 *     and no Retry, because the capability does not exist here (an outage is a different 503 and is shown, with Retry).
 *   - any other failure: a message by status (never the server's detail text); 503 and the generic failure offer Retry.
 *
 * `BillingGuidelinesView` takes an optional `coreRecordId` for the per-record view; the registered widget body shows the general view.
 * Layout: no fixed widths; text wraps (overflow-wrap) and the parameter list wraps, so a 360 px viewport scrolls only vertically.
 * ADR-021: Fluent v9 semantic tokens only.
 */
import * as React from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Subtitle2,
  Text,
  Title3,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { DocumentBulletListRegular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import {
  getBillingGuidelines,
  isBillingGuidelinesDisabledError,
  type BillingGuidelineRule,
  type BillingGuidelineSet,
  type BillingGuidelinesResponse,
} from '../api/billing-guidelines-client';
import { ApiError } from '../types';
import type { WidgetBodyComponent } from '../registry/PlaceholderWidgetBody';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    minWidth: 0,
    maxWidth: '100%',
    boxSizing: 'border-box',
  },
  firm: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 0,
  },
  set: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  setHeader: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    minWidth: 0,
  },
  noMargin: {
    margin: 0,
  },
  wrap: {
    overflowWrap: 'anywhere',
    minWidth: 0,
  },
  meta: {
    color: tokens.colorNeutralForeground3,
  },
  rules: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  rule: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    minWidth: 0,
    paddingBlockStart: tokens.spacingVerticalS,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  parameters: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
    margin: 0,
  },
  parameter: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  clause: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalM,
    ...shorthands.borderLeft(tokens.strokeWidthThick, 'solid', tokens.colorNeutralStroke1),
    color: tokens.colorNeutralForeground2,
    overflowWrap: 'anywhere',
  },
});

/** A yyyy-MM-dd date as text. It is a calendar date, not an instant, so no time zone shifts it. */
export function formatGuidelineDate(date: string | null | undefined): string {
  if (!date) return '';
  const parts = /^(\d{4})-(\d{2})-(\d{2})/.exec(date);
  if (!parts) return date;
  try {
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      timeZone: 'UTC',
    }).format(new Date(Date.UTC(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]))));
  } catch {
    return date;
  }
}

/** "Version 3 · From Jan 1, 2026" / "... to Jun 30, 2026"; empty parts are left out. */
export function guidelineSetMeta(set: Pick<BillingGuidelineSet, 'version' | 'effectiveFrom' | 'effectiveTo'>): string {
  const parts: string[] = [];
  if (set.version != null) parts.push(`Version ${set.version}`);
  if (set.effectiveFrom) {
    parts.push(
      set.effectiveTo
        ? `${formatGuidelineDate(set.effectiveFrom)} to ${formatGuidelineDate(set.effectiveTo)}`
        : `From ${formatGuidelineDate(set.effectiveFrom)}`
    );
  } else if (set.effectiveTo) {
    parts.push(`Until ${formatGuidelineDate(set.effectiveTo)}`);
  }
  return parts.join(' · ');
}

/** What a failed read says, by status (never the server's detail text). `retry` marks the states worth retrying. */
export function guidelinesFailure(status: number | undefined): { message: string; retry: boolean } {
  if (status === 404) return { message: "These billing guidelines aren't available.", retry: false };
  if (status === 403) return { message: "You don't have access to billing guidelines.", retry: false };
  if (status === 503)
    return { message: "Billing guidelines couldn't be loaded just now. Please try again.", retry: true };
  return { message: "Billing guidelines couldn't be loaded. Please try again.", retry: true };
}

type LoadState =
  | { phase: 'loading' }
  | { phase: 'ready'; response: BillingGuidelinesResponse }
  | { phase: 'disabled' }
  | { phase: 'error'; status: number | undefined };

const RuleItem: React.FC<{ rule: BillingGuidelineRule }> = ({ rule }) => {
  const styles = useStyles();
  const clause = rule.clauseQuote?.trim() ? rule.clauseQuote : null;
  const section = rule.sectionReference?.trim() ? rule.sectionReference : null;
  return (
    <li className={styles.rule} data-testid="billing-guideline-rule">
      <Text weight="semibold" className={styles.wrap}>
        {rule.name}
      </Text>
      {rule.descriptionAvailable && rule.description ? (
        <Text className={styles.wrap} data-testid="billing-guideline-description">
          {rule.description}
        </Text>
      ) : (
        <Text className={`${styles.wrap} ${styles.meta}`} data-testid="billing-guideline-no-description">
          A plain-language summary isn&apos;t available for this rule.
        </Text>
      )}
      {rule.parameters.length > 0 && (
        <dl className={styles.parameters} aria-label={`${rule.name} parameters`}>
          {rule.parameters.map(p => (
            <div key={`${p.name}:${p.value}`} className={styles.parameter} data-testid="billing-guideline-parameter">
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  {p.name}
                </Text>
              </dt>
              <dd className={`${styles.wrap} ${styles.noMargin}`}>
                <Text size={200}>{p.unit ? `${p.value} ${p.unit}` : p.value}</Text>
              </dd>
            </div>
          ))}
        </dl>
      )}
      {(clause || section) && (
        <blockquote className={styles.clause} data-testid="billing-guideline-clause">
          {clause && <Text className={styles.wrap}>{clause}</Text>}
          {section && (
            <Text size={200} block className={`${styles.wrap} ${styles.meta}`}>
              {section}
            </Text>
          )}
        </blockquote>
      )}
    </li>
  );
};

const SetSection: React.FC<{ set: BillingGuidelineSet }> = ({ set }) => {
  const styles = useStyles();
  const meta = guidelineSetMeta(set);
  return (
    <section className={styles.set} aria-label={set.name} data-testid="billing-guideline-set">
      <div className={styles.setHeader}>
        <Subtitle2 className={styles.wrap}>{set.name}</Subtitle2>
        {set.scope && (
          <Badge appearance="tint" shape="rounded" data-testid="billing-guideline-scope">
            {set.scope}
          </Badge>
        )}
      </div>
      {meta && (
        <Text size={200} className={styles.meta} data-testid="billing-guideline-meta">
          {meta}
        </Text>
      )}
      <ul className={styles.rules} aria-label={`${set.name} rules`}>
        {set.rules.map((rule, i) => (
          <RuleItem key={`${rule.name}:${i}`} rule={rule} />
        ))}
      </ul>
    </section>
  );
};

export interface BillingGuidelinesViewProps {
  /** A matter, project or work assignment the caller can read: the guidelines for that record. Omit for the general view. */
  coreRecordId?: string;
}

/** The guidelines list: loads, then shows the states in the file header. */
export const BillingGuidelinesView: React.FC<BillingGuidelinesViewProps> = ({ coreRecordId }) => {
  const styles = useStyles();
  const [state, setState] = React.useState<LoadState>({ phase: 'loading' });
  const [attempt, setAttempt] = React.useState(0);

  React.useEffect(() => {
    let cancelled = false;
    setState({ phase: 'loading' });
    getBillingGuidelines(coreRecordId).then(
      response => {
        if (!cancelled) setState({ phase: 'ready', response });
      },
      (err: unknown) => {
        if (cancelled) return;
        if (isBillingGuidelinesDisabledError(err)) {
          setState({ phase: 'disabled' });
          return;
        }
        // The status only: the error text of a BFF failure can carry detail that is not for the page or the console.
        console.error(
          `[BillingGuidelines] The guidelines could not be read (${err instanceof ApiError ? err.statusCode : 'no response'}).`
        );
        setState({ phase: 'error', status: err instanceof ApiError ? err.statusCode : undefined });
      }
    );
    return () => {
      cancelled = true;
    };
  }, [coreRecordId, attempt]);

  // The kill switch is off for this environment: the view is hidden, not shown as an outage.
  if (state.phase === 'disabled') return null;

  if (state.phase === 'loading') {
    return (
      <div className={styles.root} data-testid="billing-guidelines">
        <Spinner size="small" label="Loading billing guidelines…" labelPosition="after" />
      </div>
    );
  }

  if (state.phase === 'error') {
    const failure = guidelinesFailure(state.status);
    return (
      <div className={styles.root} data-testid="billing-guidelines">
        <MessageBar intent="error" data-testid="billing-guidelines-error">
          <MessageBarBody>{failure.message}</MessageBarBody>
        </MessageBar>
        {failure.retry && (
          <div>
            <Button appearance="secondary" size="small" onClick={() => setAttempt(n => n + 1)}>
              Retry
            </Button>
          </div>
        )}
      </div>
    );
  }

  const { sets } = state.response;
  if (sets.length === 0) {
    return (
      <div className={styles.root} data-testid="billing-guidelines">
        <EmptyState
          size="compact"
          icon={<DocumentBulletListRegular />}
          heading="No billing guidelines"
          description="No billing guidelines have been shared with you yet."
        />
      </div>
    );
  }

  // One heading per firm, in the order the server answered, only when there is more than one firm to tell apart.
  const firms: { name: string; sets: BillingGuidelineSet[] }[] = [];
  for (const set of sets) {
    const group = firms.find(f => f.name === set.firmName);
    if (group) group.sets.push(set);
    else firms.push({ name: set.firmName, sets: [set] });
  }

  return (
    <div className={styles.root} data-testid="billing-guidelines">
      {firms.map(group => (
        <div key={group.name} className={styles.firm} data-testid="billing-guideline-firm">
          {firms.length > 1 && <Title3 className={styles.wrap}>{group.name}</Title3>}
          {group.sets.map((set, i) => (
            <SetSection key={`${set.name}:${set.scope}:${i}`} set={set} />
          ))}
        </div>
      ))}
    </div>
  );
};

/** The registry body: the general view (no record). */
export const BillingGuidelinesWidgetBody: WidgetBodyComponent = () => <BillingGuidelinesView />;

export default BillingGuidelinesWidgetBody;
