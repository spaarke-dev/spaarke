/**
 * DocumentPerItemCards — document per-item ACTION CARDS (Summarize · Draft response · Draft
 * memo) keyed to the active document-viewer tab (spaarkeai-assistant-enhancements-r3 task 026,
 * FR-11).
 *
 * REACTIVE/local-selection card surface (NFR-07) — appears on tab-focus with NO typing,
 * distinct from the ADR-047 server-initiated notification spine (no merge, no new push channel;
 * this hook touches no spine code and adds no push mechanism). Mirrors `EmailPerItemCards.tsx`
 * (task 025) exactly at the surface layer (disclosure header, id-keyed cards, suppress on
 * deselect/multi/non-document active item).
 *
 * §11 reuse-first — this hook does NOT introduce a new document tool, a new selection model, or
 * a new BFF entry point. Each card sends a deterministic chat instruction through the SprkChat
 * `pendingOutboundMessage` host→send seam (`ConversationPane.tsx`'s existing "Summarize this
 * email" precedent, `LOCAL_CHIP.emailSummarize`), with the active document's id armed for that
 * ONE outbound turn via `onDecorateOutboundBody`'s `body.documentId` — the SAME one-shot,
 * ADR-015 on-demand mechanism already shipped (`pendingEmailDocRef` /
 * `handleDecorateOutboundBodyWithRevise`). The server resolves `body.documentId` through the
 * SAME `DocumentContextService`/RAG lane-2 pipeline that already backs "summarize this document"
 * today (`ChatEndpoints.SendMessage` → `effectiveDocumentId` → `agentFactory.CreateAgentAsync`):
 * NO new BFF tool, NO new retrieval service.
 *
 * Card SET + labels + `tool` keys come from task 022's frozen `WidgetAssistantContract.perItemCards`
 * on the `'document-viewer'` workspace-widget registration (`getWidgetAssistantContract('document-viewer')`,
 * `@spaarke/ai-widgets`) — NOT a hardcoded parallel list. See `register-document-viewer-widget.ts`
 * for the task-026 finalization of the `tool`/`landing` fields (landing note: all three cards land
 * `'chat'` — no composer/Compose surface exists for a bare document the way `SendEmailDialog` does
 * for email; see that file's docblock for the full CLAUDE.md §6.5 Path A writeup).
 *
 * ADR-015: cards key OFF the `{id,type,label}` handle from the active-item conduit — `id`
 * (documentId) is the ONLY identifier retained on the handle/conduit; document content is
 * tool-fetched by that id SERVER-SIDE at send time (via `body.documentId`), never carried on the
 * conduit / read off the handle's `label`.
 *
 * ASSISTANT-UI-ELEMENT-CRITERIA.md: per-item actions on an active item are CARDS (not chips) — a
 * persistent act-on affordance, not a turn-grounded next-step. The three cards collapse under ONE
 * disclosure header, mirroring the email cards' "Email actions" header (a toggle, no hover).
 */
import * as React from 'react';
import { Button, makeStyles, shorthands, tokens } from '@fluentui/react-components';
import {
  TextBulletListSquareRegular,
  ArrowReplyRegular,
  DocumentTextRegular,
  ChevronDownRegular,
  ChevronRightRegular,
} from '@fluentui/react-icons';
import { getWidgetAssistantContract, DOCUMENT_VIEWER_WIDGET_TYPE } from '@spaarke/ai-widgets';
import type { AssistantContractCard } from '@spaarke/ai-widgets';
import type { ActiveItemHandle } from '../workspace/activeItemConduit';

/** The active-item conduit's `type` discriminator for a document-viewer tab-focus handle (task 026). */
const DOCUMENT_ACTIVE_ITEM_TYPE = 'document';

/**
 * Card `tool` key → the deterministic chat instruction text this card sends. These are NOT
 * closed-catalog BFF tool names (documents have no deterministic REST entry point the way email's
 * `/api/communications/draft` does) — they are stable client-side keys from the task-022 registry
 * contract, mapped here to the SAME instruction a user could type by hand. See the file docblock
 * for the full reuse writeup.
 *
 * Wording NOTE (task 026): each instruction is deliberately phrased to fall through to the NORMAL
 * grounded agent turn (`ConversationPane.handleDecorateOutboundBodyWithRevise`'s final
 * `handleDecorateOutboundBody(body)` branch) rather than trip the EXISTING R7-4/R7-5
 * `detectDraftDocumentIntent` auto-router (`composeDraftRouting.ts`, e.g. "draft a memo" /
 * "draft ... to this document"). That router dispatches to `compose-draft-document` via a
 * SEPARATE Binding call (`chips.dispatchBinding`) that does NOT forward the one-shot
 * `body.documentId` this hook arms — routing through it would silently DROP the RAG grounding
 * (see the landing NOTE in `register-document-viewer-widget.ts` for the full CLAUDE.md §6.5 Path A
 * writeup). Keeping all three cards on the grounded chat path is the verifiably-correct choice for
 * this task; a properly-grounded Compose landing is deferred to task 040.
 */
const MESSAGE_BY_TOOL: Readonly<Record<string, string>> = {
  summarize_document: 'Summarize this document',
  draft_document_response: 'Draft a response',
  draft_document_memo: 'Draft a short internal summary for the file',
};

/** Card LABEL → icon. Presentation only. */
const ICON_BY_LABEL: Readonly<Record<string, React.ReactElement>> = {
  Summarize: <TextBulletListSquareRegular />,
  'Draft response': <ArrowReplyRegular />,
  'Draft memo': <DocumentTextRegular />,
};

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
  // Each action is a full-width clickable row — hover affordance ONLY on the clickable region
  // (ASSISTANT-UI-ELEMENT-CRITERIA.md), mirroring EmailPerItemCards' `row` style verbatim.
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
  },
});

export interface DocumentPerItemCardsDeps {
  /** The generalized active-item handle (task 001/026). Cards render only when `type === 'document'`. */
  activeItem: ActiveItemHandle | null;
  /**
   * Arm the NEXT outbound chat turn with `documentId` (one-shot, ADR-015 on-demand — never
   * persisted beyond that turn) and send `message` through the host's existing
   * `pendingOutboundMessage` seam (the SAME mechanism `LOCAL_CHIP.emailSummarize` already uses).
   * Owned by `ConversationPane` (it holds the ref + state this arms) — injected so this hook stays
   * self-contained and independently testable.
   */
  armDocumentTurn: (documentId: string, message: string) => void;
}

export interface DocumentPerItemCardsController {
  /** Memoized render node for the three cards under one disclosure header — null when suppressed. */
  readonly cardSlot: React.ReactNode;
}

/**
 * `useDocumentPerItemCards` — the card-surface controller. Mirrors `useEmailPerItemCards`'s shape
 * (a `cardSlot` render node the host mounts in its transcript-footer region), minus a composer
 * dialog — no document-composer surface exists (see the landing NOTE in
 * `register-document-viewer-widget.ts`), so every card lands via `armDocumentTurn` alone.
 */
export function useDocumentPerItemCards(deps: DocumentPerItemCardsDeps): DocumentPerItemCardsController {
  const { activeItem, armDocumentTurn } = deps;
  const styles = useStyles();

  const [expanded, setExpanded] = React.useState(true);

  const activeDocument = activeItem?.type === DOCUMENT_ACTIVE_ITEM_TYPE ? activeItem : null;

  const handleCardClick = React.useCallback(
    (card: AssistantContractCard) => {
      if (!activeDocument) return;
      // ADR-015: only the id travels here (from the conduit handle) — content is fetched
      // SERVER-SIDE by that id when the armed turn sends (never read off `activeDocument.label`).
      const message = MESSAGE_BY_TOOL[card.tool] ?? card.label;
      armDocumentTurn(activeDocument.id, message);
    },
    [activeDocument, armDocumentTurn]
  );

  const cardSlot = React.useMemo<React.ReactNode>(() => {
    if (!activeDocument) return null;
    const contract = getWidgetAssistantContract(DOCUMENT_VIEWER_WIDGET_TYPE);
    const cards = contract?.perItemCards ?? [];
    if (cards.length === 0) return null;

    return (
      <div className={styles.panel} data-testid="document-per-item-cards">
        <Button
          className={styles.header}
          appearance="transparent"
          size="small"
          icon={expanded ? <ChevronDownRegular /> : <ChevronRightRegular />}
          iconPosition="before"
          onClick={() => setExpanded(e => !e)}
          aria-expanded={expanded}
          data-testid="document-per-item-cards-toggle"
        >
          Document actions
        </Button>
        {expanded ? (
          <div className={styles.body}>
            {cards.map(card => (
              <Button
                key={card.label}
                className={styles.row}
                appearance="transparent"
                shape="rounded"
                icon={ICON_BY_LABEL[card.label]}
                iconPosition="before"
                onClick={() => handleCardClick(card)}
                data-testid={`document-card-${card.tool}-${card.label.replace(/\s+/g, '-').toLowerCase()}`}
              >
                {card.label}
              </Button>
            ))}
          </div>
        ) : null}
      </div>
    );
  }, [activeDocument, expanded, handleCardClick, styles]);

  return React.useMemo(() => ({ cardSlot }), [cardSlot]);
}
