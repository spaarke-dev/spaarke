/**
 * ProactiveCardStack — collapses MULTIPLE simultaneous proactive card slots
 * behind ONE disclosure header (spaarkeai-assistant-enhancements-r3 task 041,
 * FR-14).
 *
 * ASSISTANT-UI-ELEMENT-CRITERIA.md §5: "DO collapse a *stack* of proactive
 * cards behind a single disclosure header ... so they don't dominate the
 * conversation space ... the header is a toggle (no hover), the cards drop
 * down." Today `ConversationPane.tsx`'s transcript footer can render UP TO
 * THREE independent card slots at once — `useRerunFullAnalysisCard`'s
 * single-row "Rerun a full analysis" card (no header of its own) alongside
 * `useEmailPerItemCards`/`useDocumentPerItemCards`'s own already-headered
 * ("Email actions" / "Document actions") panels (task 025/026) — e.g. a quick
 * NDA review just completed WHILE an email tab with an active email is also
 * open. Before this component, that combination stacked as two-to-three
 * separate boxes with no unifying disclosure. This component is a PURE
 * presentational wrapper — it does NOT reach into 025/026's internals or
 * change their own (already-correct, per-panel) headers; it only decides
 * whether an OUTER header is needed:
 *   - 0 slots present  → renders nothing.
 *   - 1 slot present   → renders that slot directly, unwrapped (today's
 *     overwhelmingly common case — a lone card should not gain a redundant
 *     outer header).
 *   - 2+ slots present → wraps them behind ONE outer disclosure header
 *     ("You have N pending actions"), a toggle with no hover styling per the
 *     criteria doc; expanding reveals the full stack.
 *
 * §11 reuse-first: this does not duplicate `EmailPerItemCards.tsx` /
 * `DocumentPerItemCards.tsx` / `useRerunFullAnalysisCard.tsx`'s own disclosure
 * chrome — those stay exactly as shipped. This is the ONE new outer layer.
 */
import * as React from 'react';
import { Button, makeStyles, shorthands, tokens } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronRightRegular } from '@fluentui/react-icons';

export interface ProactiveCardSlot {
  /** Stable React key for this slot (e.g. 'rerun-full-analysis', 'email-per-item', 'document-per-item'). */
  readonly key: string;
  /** The slot's render node — `null`/`undefined` means "not currently offered", filtered out before counting. */
  readonly node: React.ReactNode;
}

export interface ProactiveCardStackProps {
  readonly slots: ReadonlyArray<ProactiveCardSlot>;
}

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
  },
  // The OUTER disclosure header — a TOGGLE, deliberately no hover highlight
  // (ASSISTANT-UI-ELEMENT-CRITERIA.md "the header is a toggle (no hover)"),
  // mirroring EmailPerItemCards/DocumentPerItemCards' own header style.
  header: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    width: '100%',
    justifyContent: 'flex-start',
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground2,
    ...shorthands.border('none'),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    cursor: 'pointer',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
  },
});

/**
 * `ProactiveCardStack` — renders `slots` unwrapped when there is 0 or 1
 * present; collapses 2+ behind one disclosure header. Expanded by default
 * (mirrors `EmailPerItemCards`/`DocumentPerItemCards`'s own default-expanded
 * headers) so the stack is never hidden on first render.
 */
export function ProactiveCardStack({ slots }: ProactiveCardStackProps): React.ReactElement | null {
  const styles = useStyles();
  const [expanded, setExpanded] = React.useState(true);

  const present = React.useMemo(
    () => slots.filter((s): s is ProactiveCardSlot & { node: React.ReactNode } => s.node != null),
    [slots]
  );

  if (present.length === 0) return null;

  if (present.length === 1) {
    // eslint-disable-next-line react/jsx-no-useless-fragment
    return <React.Fragment>{present[0].node}</React.Fragment>;
  }

  return (
    <div className={styles.wrapper} data-testid="proactive-card-stack">
      <Button
        className={styles.header}
        appearance="transparent"
        size="small"
        icon={expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
        iconPosition="before"
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
        data-testid="proactive-card-stack-toggle"
      >
        {`You have ${present.length} pending actions`}
      </Button>
      {expanded ? (
        <div className={styles.body}>
          {present.map((s) => (
            <React.Fragment key={s.key}>{s.node}</React.Fragment>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export default ProactiveCardStack;
