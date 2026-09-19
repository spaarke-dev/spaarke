import React, { useEffect, useRef } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Body1,
  Badge,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular } from '@fluentui/react-icons';
import { DOCUMENT_SUMMARY_STATUS_MESSAGES } from '../services/documentProfileChoices';
import { useDocumentProfile } from '../hooks/useDocumentProfile';

/**
 * DocumentProfileSection.tsx — spaarkeai-word-add-in-r1 task 021 (FR-07) + task 022 (FR-08).
 *
 * Renders the Save tab's "Profile" section: read-only `sprk_filesummary`, `sprk_filetldr`,
 * `sprk_filekeywords`, `sprk_documenttype` for a resolved `sprk_document`, or an honest state
 * message for each of the six non-Completed `sprk_filesummarystatus` values, or a no-identity
 * state when task 013 resolved nothing — plus (task 022) a "Generate Profile" control that
 * re-dispatches profiling for the current document.
 *
 * SCOPE (spec Assumptions): the four profile FIELDS are READ-ONLY — no edit affordance, no
 * pencil icon, no save-back; editing happens in the record (FR-10 / task 027). Generate Profile is
 * not an edit of a field, it is an ACTION that re-runs the AI pipeline server-side; per spec
 * Assumptions it OVERWRITES the existing profile with NO confirmation prompt — this component must
 * never add one.
 *
 * Theming (ADR-021): tokens.* only, no hex literals. This component does not call
 * `useOfficeTheme` directly — the pane's Fluent `FluentProvider` (rooted in `App.tsx` via
 * `useTheme`, which runs the same Office.js theme-bridge detection as `useOfficeTheme`) already
 * resolves every `tokens.*` reference to the correct light/dark/high-contrast value for the whole
 * tree; a second, independent theme subscription in this leaf component would not change what
 * renders (SaveFlow.tsx, the sibling this section lives inside, follows the same convention). The
 * Generate Profile button's default/busy/disabled states resolve colors from Fluent's Button
 * appearance + `tokens.*` (the empty-value hint text) the same way.
 */

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXS,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  fieldContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // Fluent v9 host-visual-fit pattern (Office add-in narrow pane): long AI summary text must
    // wrap and scroll vertically, never force horizontal overflow.
    minWidth: 0,
    overflowWrap: 'break-word',
    wordBreak: 'break-word',
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  fieldValue: {
    whiteSpace: 'pre-wrap',
    maxHeight: '220px',
    overflowY: 'auto',
  },
  keywordList: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
  emptyValue: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXS,
  },
  disabledReason: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

export interface DocumentProfileSectionProps {
  /**
   * The `sprk_document` id resolved by task 013 (bare or braced — canonicalized internally via
   * `cleanGuid`, ADR-044). `undefined` renders the no-identity state and makes no network call.
   */
  documentId?: string;
  /**
   * task 027 / FR-10 return-path signal: `SaveFlow` increments this counter on a
   * focus/visibility-triggered pane return (after the user opened the `sprk_document` record or
   * its related record via the browser-tab escape hatch — Spike-2 Option 3). Any change past the
   * initial mount re-reads the profile via `useDocumentProfile`'s `refetch()` (task 033), so an
   * edit made in the opened record is reflected here without a manual refresh. `undefined`/absent
   * is a no-op — this section behaves exactly as before task 027 when no signal is threaded in.
   */
  refreshSignal?: number;
}

/** Splits the comma-separated `sprk_filekeywords` value into individual chip labels. */
function splitKeywords(keywords: string): string[] {
  return keywords
    .split(',')
    .map(k => k.trim())
    .filter(k => k.length > 0);
}

export function DocumentProfileSection({ documentId, refreshSignal }: DocumentProfileSectionProps): React.ReactElement {
  const styles = useStyles();
  const { outcome, generateProfile, isGenerating, generateError, refetch } = useDocumentProfile(documentId);

  // task 027 / FR-10 return path: re-read on every CHANGE of refreshSignal past the initial mount
  // (`useDocumentProfile`'s own documentId-keyed effect already covers first load — calling
  // refetch() again on mount would just be a redundant duplicate request). Deliberately depends
  // ONLY on refreshSignal, not on refetch: refetch's identity is recreated whenever `documentId`
  // changes (it is a useCallback keyed on documentId inside the hook), and documentId's OWN change
  // is already handled by useDocumentProfile's internal effect — including refetch as a dependency
  // here would re-fire this effect (and issue a DUPLICATE, racing fetch) on every documentId
  // change too, not just on a genuine refreshSignal bump (caught by DocumentProfileSection.test.tsx
  // "re-fetches when documentId changes" during this task's own verification).
  const isFirstRefreshRender = useRef(true);
  useEffect(() => {
    if (isFirstRefreshRender.current) {
      isFirstRefreshRender.current = false;
      return;
    }
    refetch();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- see comment above: refetch is
    // intentionally excluded.
  }, [refreshSignal]);

  // Disabled without a resolved identity (task 013 resolved nothing) or while a request is already
  // in flight. Never disabled merely because the current status is Completed — FR-08/spec
  // Assumptions require Generate Profile to remain available (and to overwrite silently) for a
  // document that already has a profile.
  const isDisabled = !documentId || isGenerating;

  return (
    <div className={styles.section}>
      <div className={styles.sectionTitle}>
        <Text weight="semibold">Profile</Text>
      </div>
      <div className={styles.card}>{renderBody()}</div>
      <div className={styles.actions}>
        <Button
          appearance="secondary"
          size="small"
          icon={isGenerating ? <Spinner size="tiny" /> : <ArrowClockwiseRegular />}
          disabled={isDisabled}
          onClick={() => {
            void generateProfile();
          }}
        >
          {isGenerating ? 'Generating…' : 'Generate Profile'}
        </Button>
        {!documentId && (
          <Text size={200} className={styles.disabledReason}>
            This document is not yet in Spaarke. Save it first to generate a profile.
          </Text>
        )}
        {generateError && (
          <MessageBar intent="error">
            <MessageBarBody>{generateError}</MessageBarBody>
          </MessageBar>
        )}
      </div>
    </div>
  );

  function renderBody(): React.ReactElement {
    switch (outcome.kind) {
      case 'no-identity':
        return (
          <Text size={200} className={styles.emptyValue}>
            This document is not yet in Spaarke. Save it first to see its AI profile here.
          </Text>
        );

      case 'loading':
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
            <Spinner size="tiny" />
            <Text size={200}>Loading profile…</Text>
          </div>
        );

      case 'error':
        return (
          <MessageBar intent="error">
            <MessageBarBody>{outcome.message}</MessageBarBody>
          </MessageBar>
        );

      case 'status':
        return <Text size={200}>{DOCUMENT_SUMMARY_STATUS_MESSAGES[outcome.status]}</Text>;

      case 'completed': {
        const keywords = splitKeywords(outcome.keywords);
        return (
          <>
            <div className={styles.fieldContainer}>
              <Text className={styles.fieldLabel}>Document type</Text>
              {outcome.documentType ? (
                <Text>{outcome.documentType}</Text>
              ) : (
                <Text size={200} className={styles.emptyValue}>
                  Not classified.
                </Text>
              )}
            </div>

            <div className={styles.fieldContainer}>
              <Text className={styles.fieldLabel}>TL;DR</Text>
              {outcome.tldr ? (
                <Body1 className={styles.fieldValue}>{outcome.tldr}</Body1>
              ) : (
                <Text size={200} className={styles.emptyValue}>
                  No TL;DR generated.
                </Text>
              )}
            </div>

            <div className={styles.fieldContainer}>
              <Text className={styles.fieldLabel}>Summary</Text>
              {outcome.summary ? (
                <Body1 className={styles.fieldValue}>{outcome.summary}</Body1>
              ) : (
                <Text size={200} className={styles.emptyValue}>
                  No summary generated.
                </Text>
              )}
            </div>

            <div className={styles.fieldContainer}>
              <Text className={styles.fieldLabel}>Keywords</Text>
              {keywords.length > 0 ? (
                <div className={styles.keywordList} role="list" aria-label="Keywords">
                  {keywords.map(keyword => (
                    <Badge key={keyword} appearance="tint" color="informative" role="listitem">
                      {keyword}
                    </Badge>
                  ))}
                </div>
              ) : (
                <Text size={200} className={styles.emptyValue}>
                  No keywords generated.
                </Text>
              )}
            </div>
          </>
        );
      }

      default:
        return <></>;
    }
  }
}

export default DocumentProfileSection;
