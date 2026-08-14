/**
 * EmailPerItemCards — email per-item ACTION CARDS (Reply · Reply All · Forward ·
 * Summarize the thread) keyed to the active email (spaarkeai-assistant-enhancements-r3
 * task 025, FR-09/FR-10).
 *
 * REACTIVE/local-selection card surface (NFR-07) — appears on selection with NO
 * typing, distinct from the ADR-047 server-initiated notification spine (no merge,
 * no new push channel; this hook touches no spine code and adds no push mechanism).
 *
 * §11 reuse-first — this hook does NOT re-implement anything task 022/023/024 already
 * shipped; it wires them together:
 *   - Card SET + labels + landing (chat vs composer) come from task 022's frozen
 *     `WidgetAssistantContract.perItemCards` on the `'email'` workspace-widget
 *     registration (`getWidgetAssistantContract('email')`, `@spaarke/ai-widgets`) —
 *     NOT a hardcoded parallel list. Adding/renaming a card in the registry changes
 *     what renders here with zero code change.
 *   - The AI auto-draft/summarize capability is the SAME `IEmailDraftAi` facade
 *     task 023's `EmailDraftToolHandler` wraps as chat-agent tools
 *     (`email.draft_reply`/`draft_forward`/`summarize_thread`, Intent
 *     'reply'/'custom'+forward-note/'summarize') — reached here via the EXISTING
 *     `POST /api/communications/draft` REST endpoint
 *     (`createXrmEmailComposeHandlers(...).onDraftWithAi`, `@spaarke/ui-components`),
 *     which is the SAME call the composer's in-dialog "sparkle" already makes.
 *     DEVIATION NOTE: `email.draft_reply`/`draft_forward`/`summarize_thread` are
 *     `sprk_availableincontexts = Chat only` (agent-loop tools) — there is no
 *     deterministic client-callable invocation for them outside an LLM chat turn,
 *     and this task's constraints forbid touching BFF C#. Calling the identical
 *     `IEmailDraftAi` facade via the pre-existing `/api/communications/draft`
 *     endpoint reaches the SAME AI behavior (same facade, same Intent semantics)
 *     deterministically from a card click. See task notes for the full ADR-conflict
 *     writeup (CLAUDE.md §6.5 Path C — pivot to the established REST extension point).
 *   - The composer opens via task 024's `useEmailComposeActions.openComposer(mode,
 *     communicationId, { bodyOverride })` — thread-preserving (FR-10), including the
 *     D-5 `initialQuotedThread` fix so an in-dialog re-sparkle cannot drop the thread.
 *   - Reading the source email's current subject/body for the AI call reuses
 *     `fetchCommunicationPrefill` (task 022/024, `@spaarke/communication-components`)
 *     — the SAME Xrm-backed read `useEmailComposeActions.openComposer` already does
 *     internally; no new Dataverse read path.
 *
 * ADR-015: cards key OFF the `{id,type,label}` handle from the active-item conduit —
 * `id` (communicationId) is the ONLY identifier retained; email content is
 * tool-fetched by that id (via `fetchCommunicationPrefill`) at CLICK time, never
 * carried on the conduit / a prompt.
 *
 * ASSISTANT-UI-ELEMENT-CRITERIA.md: per-item actions on an active item are CARDS
 * (not chips) — a persistent act-on affordance, not a turn-grounded next-step. The
 * four cards collapse under ONE disclosure header (a toggle, no hover) so they don't
 * dominate the transcript footer alongside the reactive chip strip.
 */
import * as React from 'react';
import { Button, makeStyles, shorthands, tokens, Spinner, Text } from '@fluentui/react-components';
import {
  ArrowReplyRegular,
  ArrowReplyAllRegular,
  ArrowForwardRegular,
  TextBulletListSquareRegular,
  ChevronDownRegular,
  ChevronRightRegular,
} from '@fluentui/react-icons';
import { getWidgetAssistantContract } from '@spaarke/ai-widgets';
import type { AssistantContractCard } from '@spaarke/ai-widgets';
import {
  useEmailComposeActions,
  fetchCommunicationPrefill,
} from '@spaarke/communication-components';
import {
  createXrmEmailComposeHandlers,
  createXrmDataService,
  createXrmNavigationService,
} from '@spaarke/ui-components';
import type {
  AuthenticatedFetchFn,
  IChatMessage,
  ILookupItem,
  IDataService,
  INavigationService,
} from '@spaarke/ui-components';
import type { ComposerMode } from '@spaarke/communication-components';
import type { ActiveItemHandle } from '../workspace/activeItemConduit';
// §11 reuse — the SAME `IChatMessage` builder every other local-injection call site in
// ConversationPane.tsx uses (`makeLocalAssistantMessage`), not a second copy.
import { makeLocalAssistantMessage } from './summarizeRouting';

/** The `'email'` widget-registry type key (task 020/022 — `contextType: 'email'`). */
const EMAIL_WIDGET_TYPE = 'email';

/**
 * Forwarding-note instruction — BYTE-IDENTICAL to `EmailDraftToolHandler.ExecuteComposeAsync`'s
 * `MethodForward` `UserInstruction` (server-side, `email.draft_forward`), so the client-invoked
 * `/api/communications/draft` call produces the same AI behavior the chat tool would.
 */
const FORWARD_NOTE_INSTRUCTION =
  "Write a brief, professional note to accompany forwarding this email to a new recipient. " +
  "Introduce the forwarded message in one or two sentences. Do NOT restate or quote the full " +
  "message — the original thread is preserved separately below your note.";

/**
 * Card LABEL → composer `ComposerMode`. The registry contract (task 022) declares
 * `label`/`tool`/`landing` only — it has no compose-engine concept of `ComposerMode`
 * (Reply vs Reply All share the SAME `draft_reply` tool; mode is a call-time
 * distinction the composer needs, not part of the tool identity). Keyed on the
 * STATIC display label from the frozen contract (never a model-controlled value).
 */
const COMPOSER_MODE_BY_LABEL: Readonly<Record<string, ComposerMode>> = {
  Reply: 'reply',
  'Reply All': 'replyAll',
  Forward: 'forward',
};

/** Card LABEL → icon. Presentation only. */
const ICON_BY_LABEL: Readonly<Record<string, React.ReactElement>> = {
  Reply: <ArrowReplyRegular />,
  'Reply All': <ArrowReplyAllRegular />,
  Forward: <ArrowForwardRegular />,
  'Summarize the thread': <TextBulletListSquareRegular />,
};

/** Tool NAME → the `IEmailDraftAi` Intent + optional fixed instruction (mirrors task 023 exactly). */
function draftRequestForTool(tool: string): { intent: string; userInstruction?: string; isHtml: boolean } {
  switch (tool) {
    case 'draft_forward':
      return { intent: 'custom', userInstruction: FORWARD_NOTE_INSTRUCTION, isHtml: true };
    case 'summarize_thread':
      // Plain narrative out (file-summarize shape) regardless of source format — mirrors
      // ExecuteSummarizeThreadAsync's IsHtml=false.
      return { intent: 'summarize', isHtml: false };
    case 'draft_reply':
    default:
      return { intent: 'reply', isHtml: true };
  }
}

const useStyles = makeStyles({
  panel: {
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: 'hidden',
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalS,
  },
  // Disclosure header — a TOGGLE, deliberately no hover highlight (ASSISTANT-UI-ELEMENT-CRITERIA.md
  // "the header is a toggle (no hover)"). Plain text + chevron, minimal chrome.
  header: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    width: '100%',
    justifyContent: 'flex-start',
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground2,
    ...shorthands.border('none'),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    cursor: 'pointer',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
  },
  // Each action is a full-width clickable row, mirroring SuggestionCard's `main` button
  // (hover affordance ONLY on the clickable region — ASSISTANT-UI-ELEMENT-CRITERIA.md).
  row: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    columnGap: tokens.spacingHorizontalS,
    width: '100%',
    color: tokens.colorNeutralForeground1,
    backgroundColor: 'transparent',
    borderRadius: 0,
    ...shorthands.border('none'),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':hover:active': { backgroundColor: tokens.colorNeutralBackground1Pressed },
    ':disabled': { color: tokens.colorNeutralForegroundDisabled, backgroundColor: 'transparent' },
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
  },
});

export interface EmailPerItemCardsDeps {
  /** The generalized active-item handle (task 001/011/012). Cards render only when `type === 'email'`. */
  activeItem: ActiveItemHandle | null;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  /** Recipient directory typeahead — reused for parity with the reading-pane composer. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /** landing='chat' (Summarize the thread) — inject a plain-narrative assistant message into the transcript. */
  enqueueAssistantMessage: (message: IChatMessage) => void;
}

export interface EmailPerItemCardsController {
  /** Memoized render node for the four cards under one disclosure header — null when suppressed. */
  readonly cardSlot: React.ReactNode;
  /** The composer dialog element — render this once, anywhere below the host FluentProvider. */
  readonly composerDialog: React.ReactElement;
}

/**
 * `useEmailPerItemCards` — the card-surface controller. Mirrors `useRerunFullAnalysisCard`'s
 * shape (a `cardSlot` render node the host mounts in its transcript-footer region) plus a
 * `composerDialog` element (mirrors `useEmailComposeActions`'s own contract), since this
 * controller owns its own composer instance (record-scoped Reply/Reply All/Forward, distinct
 * from the pane's existing "New" `SendEmailDialog`/`emailSeed` compose-only instance).
 */
export function useEmailPerItemCards(deps: EmailPerItemCardsDeps): EmailPerItemCardsController {
  const { activeItem, authenticatedFetch, bffBaseUrl, onSearchRecipients, enqueueAssistantMessage } = deps;
  const styles = useStyles();

  // One Xrm-backed adapter pair, scoped to this controller (mirrors EmailWorkspaceWidget's own
  // "instantiate once" per-mount pattern) — separate from any pre-existing ConversationPane
  // instances so this hook stays self-contained and independently testable.
  const dataService = React.useMemo<IDataService>(() => createXrmDataService(), []);
  const navigationService = React.useMemo<INavigationService>(() => createXrmNavigationService(), []);
  const composeHandlers = React.useMemo(
    () => createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  const { openComposer, composerDialog } = useEmailComposeActions({
    authenticatedFetch,
    bffBaseUrl,
    dataService,
    navigationService,
    onSearchRecipients,
    onLookupRecipients: composeHandlers.onLookupRecipients,
    recordLookupCatalog: composeHandlers.recordLookupCatalog,
    onLookupRecord: composeHandlers.onLookupRecord,
    onAddRelationship: composeHandlers.onAddRelationship,
    onUploadLocalAttachment: composeHandlers.onUploadLocalAttachment,
    onResolveShareLink: composeHandlers.onResolveShareLink,
    onListEmailTemplates: composeHandlers.onListEmailTemplates,
    onRenderEmailTemplate: composeHandlers.onRenderEmailTemplate,
    onDraftWithAi: composeHandlers.onDraftWithAi,
  });

  const [expanded, setExpanded] = React.useState(true);
  const [busyLabel, setBusyLabel] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const activeEmail = activeItem?.type === EMAIL_WIDGET_TYPE ? activeItem : null;

  // Reset transient state when the active email changes (or clears) so a stale error/spinner
  // from a PRIOR email never bleeds into the next selection.
  const activeEmailId = activeEmail?.id ?? null;
  React.useEffect(() => {
    setBusyLabel(null);
    setError(null);
  }, [activeEmailId]);

  const handleCardClick = React.useCallback(
    async (card: AssistantContractCard) => {
      if (!activeEmail) return;
      setBusyLabel(card.label);
      setError(null);
      try {
        // ADR-015: content is fetched BY id at click time — never read off the conduit handle
        // (which carries id/type/label only) or a prompt/tab snapshot.
        const prefill = await fetchCommunicationPrefill(dataService, activeEmail.id);
        const { intent, userInstruction, isHtml } = draftRequestForTool(card.tool);
        const draftResult = await composeHandlers.onDraftWithAi?.({
          intent,
          userInstruction,
          currentBody: prefill.body,
          isHtml,
          subject: prefill.subject,
        });
        if (!draftResult || !draftResult.text) {
          setError('AI drafting is unavailable right now. Please try again later.');
          return;
        }

        if (card.landing === 'composer') {
          const mode = COMPOSER_MODE_BY_LABEL[card.label];
          if (!mode) return; // defensive — the frozen contract never declares an unmapped label
          // FR-10: seeds ONLY the authored region; the composer still derives + appends the
          // reducer's quoted thread below it (task 024's bodyOverride + the D-5 fix).
          openComposer(mode, activeEmail.id, { bodyOverride: draftResult.text });
        } else {
          // landing === 'chat' (Summarize the thread) — a plain-narrative assistant message,
          // identical in shape to a file summary. No composer opens.
          enqueueAssistantMessage(makeLocalAssistantMessage(draftResult.text));
        }
      } catch {
        setError('Something went wrong. Please try again.');
      } finally {
        setBusyLabel(null);
      }
    },
    [activeEmail, dataService, composeHandlers, openComposer, enqueueAssistantMessage]
  );

  const cardSlot = React.useMemo<React.ReactNode>(() => {
    if (!activeEmail) return null;
    const contract = getWidgetAssistantContract(EMAIL_WIDGET_TYPE);
    const cards = contract?.perItemCards ?? [];
    if (cards.length === 0) return null;

    return (
      <div className={styles.panel} data-testid="email-per-item-cards">
        <Button
          className={styles.header}
          appearance="transparent"
          size="small"
          icon={expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
          iconPosition="before"
          onClick={() => setExpanded(e => !e)}
          aria-expanded={expanded}
          data-testid="email-per-item-cards-toggle"
        >
          Email actions
        </Button>
        {expanded ? (
          <div className={styles.body}>
            {cards.map(card => (
              <Button
                key={card.label}
                className={styles.row}
                appearance="transparent"
                shape="rounded"
                icon={busyLabel === card.label ? <Spinner size="tiny" /> : ICON_BY_LABEL[card.label]}
                iconPosition="before"
                disabled={busyLabel !== null}
                onClick={() => void handleCardClick(card)}
                data-testid={`email-card-${card.tool}-${card.label.replace(/\s+/g, '-').toLowerCase()}`}
              >
                {card.label}
              </Button>
            ))}
            {error ? (
              <Text className={styles.error} role="alert">
                {error}
              </Text>
            ) : null}
          </div>
        ) : null}
      </div>
    );
  }, [activeEmail, expanded, busyLabel, error, handleCardClick, styles]);

  return React.useMemo(() => ({ cardSlot, composerDialog }), [cardSlot, composerDialog]);
}
