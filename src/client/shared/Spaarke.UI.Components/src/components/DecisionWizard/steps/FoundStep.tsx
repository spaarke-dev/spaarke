/**
 * FoundStep - "What was found": the rule's sentence (literal), the evidence behind it, how it was determined, and the other
 * open items on the same core record in the same lane.
 *
 * D-23: the sentence stays literal and carries no witness value; the values are EVIDENCE LINES (FR-48). A closed item shows the
 * Decision Record's fact snapshot - the facts as they stood when the record was written - NOT today's data. A fact that is
 * missing renders as Missing, never as zero (H-5; `EvidenceLine` owns that). "The matter" is the item's CORE record, whatever
 * its type (D-34); a Not filed item (D-35) has no core and shows no other items. The subject's name is a link that opens the
 * record in a modal above the wizard when the host provides `openRecord` and the subject has an id (D-182); the wizard keeps
 * its place and its typed values. Otherwise it is plain text.
 *
 * Two optional seams for a host that cannot hold every line or open a record in a modal (the MCP widget): `evidenceLinks` gives an evidence
 * line that names a record (`ref` = `{table}:{guid}`, as the evaluator writes it) an "Open in Spaarke" link through `openRecord`, and
 * `item.evidenceTruncation` says "Showing N of M" under the lines. A line the server withheld never links. Absent: neither, as before.
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Badge,
  Body1,
  Caption1,
  Link,
  Subtitle2,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { cleanGuid } from '../../../utils/guid';
import { EvidenceLine } from '../../ConsoleKit/EvidenceLine';
import { composeIssueLine } from '../../Worklist/composeIssueLine';
import { useDecisionItem } from '../decisionItemContext';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL },
  header: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXS },
  meta: { color: tokens.colorNeutralForeground3 },
  sentence: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalM,
    borderLeft: `${tokens.strokeWidthThick} solid ${tokens.colorNeutralStroke1}`,
    color: tokens.colorNeutralForeground1,
  },
  section: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXS },
  clause: { display: 'flex', flexDirection: 'column', paddingBlock: tokens.spacingVerticalXS },
  formal: { fontFamily: tokens.fontFamilyMonospace, color: tokens.colorNeutralForeground3 },
  otherList: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
  lineLink: { alignSelf: 'flex-start' },
});

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const LOGICAL_NAME = /^[a-z][a-z0-9_]*$/;

/**
 * The record an evidence line names: its `ref` is `{table}:{guid}` (`SignalEvaluator`). Anything else (a snapshot, a document, a ref that
 * is not that shape) names no record, so it gets no link: the type is never guessed.
 */
function evidenceTarget(ref: string): { entity: string; id: string } | null {
  const at = ref.indexOf(':');
  if (at <= 0) return null;
  const entity = ref.slice(0, at).trim().toLowerCase();
  const id = cleanGuid(ref.slice(at + 1));
  return LOGICAL_NAME.test(entity) && GUID.test(id) ? { entity, id } : null;
}

export const FoundStep: React.FC = () => {
  const { item, plan, resolution, closedIds, today, openRecord, evidenceLinks } = useDecisionItem();
  // Items closed in this session (recorded here, or resolved by Also resolve) are no longer open.
  const otherOpen = item.otherOpenItems.filter(o => !closedIds.has(o.signalId));
  const styles = useStyles();
  const line = composeIssueLine(item.row, today);
  const closed = resolution != null;
  const evidence = resolution?.factSnapshot ?? item.evidence;
  // The note is about the item's own lines: a closed item shows its stored snapshot, which the host does not bound.
  const truncation = !resolution?.factSnapshot ? (item.evidenceTruncation ?? null) : null;
  const clauses = plan.ruleDescription?.clauses ?? [];
  const coreName = item.core?.name?.trim() || null;
  const coreLabel = item.core?.typeLabel?.trim() || 'record';
  const coreType = item.core?.recordType.trim() || '';
  const coreId = item.core?.recordId.trim() || '';
  // D-182: the subject is a link only when the host can open it and the subject has an entity and an id.
  const canOpenSubject = openRecord != null && coreName != null && coreType !== '' && coreId !== '';
  const open = (entity: string, id: string) => {
    if (!openRecord) return;
    // The record opens in a platform modal above the wizard (or wherever the host sends it); a failure to open changes nothing here.
    try {
      void Promise.resolve(openRecord(entity, id)).catch(err => {
        console.warn('[DecisionWizard] openRecord failed', err);
      });
    } catch (err) {
      // The wizard keeps its place.
      // eslint-disable-next-line no-restricted-syntax -- opening the record is a convenience link; the wizard keeps its step and values, and the failure is logged
      console.warn('[DecisionWizard] openRecord failed', err);
    }
  };
  const openSubject = () => open(coreType, coreId);
  const metaParts: React.ReactNode[] = [
    ...[line.ruleShortName, line.timing.text].filter(Boolean),
    coreName ? (
      canOpenSubject ? (
        <React.Fragment key="subject">
          {coreLabel}: <Link onClick={openSubject}>{coreName}</Link>
        </React.Fragment>
      ) : (
        `${coreLabel}: ${coreName}`
      )
    ) : (
      'Not filed on a record'
    ),
  ];

  return (
    <div className={styles.root} data-testid="step-found">
      <div className={styles.header}>
        <Text as="h2" size={500} weight="semibold">
          What was found
        </Text>
        <Subtitle2>{line.headline}</Subtitle2>
        <Caption1 className={styles.meta} data-testid="found-meta">
          {metaParts.map((part, i) => (
            <React.Fragment key={i}>
              {i > 0 ? ' · ' : null}
              {part}
            </React.Fragment>
          ))}
        </Caption1>
      </div>

      {item.sentence ? (
        <blockquote className={styles.sentence} data-testid="rule-sentence">
          <Body1>{item.sentence}</Body1>
        </blockquote>
      ) : null}

      <div className={styles.section} data-testid="evidence">
        <Text weight="semibold">Evidence</Text>
        {closed && resolution.factSnapshot ? (
          <Caption1 className={styles.meta} data-testid="snapshot-note">
            The facts as they stood when this was recorded, not today&apos;s data.
          </Caption1>
        ) : null}
        {evidence.length === 0 ? (
          // Nothing shown is not "nothing recorded" when the host could not bring the lines (the note below says so).
          truncation ? null : (
            <Caption1 className={styles.meta}>No evidence was recorded for this item.</Caption1>
          )
        ) : (
          evidence.map((e, i) => {
            // A withheld line is on a record the reader may not see: it never links.
            const target = evidenceLinks && openRecord && !e.withheld ? evidenceTarget(e.ref) : null;
            return (
              <EvidenceLine
                key={`${e.kind}-${e.ref}-${i}`}
                tier={e.tier}
                text={e.text}
                subject={e.subject}
                withheld={e.withheld}
                source={e.source}
                asOf={e.asOf}
                clause={e.clause}
                actions={
                  target ? (
                    <Link
                      className={styles.lineLink}
                      aria-label={e.text ? `Open in Spaarke: ${e.text}` : undefined}
                      onClick={() => open(target.entity, target.id)}
                    >
                      Open in Spaarke
                    </Link>
                  ) : undefined
                }
              />
            );
          })
        )}
        {truncation ? (
          <Caption1 className={styles.meta} data-testid="evidence-truncated" role="status">
            {truncation.total != null && truncation.total > evidence.length
              ? `Showing ${evidence.length} of ${truncation.total} evidence lines.`
              : evidence.length === 0
                ? 'The evidence for this item is too large to show here.'
                : `Showing the first ${evidence.length} evidence lines; more are not shown.`}
            {truncation.openAll ? (
              <>
                {' '}
                <Link onClick={truncation.openAll}>Open in Spaarke</Link>
              </>
            ) : null}
          </Caption1>
        ) : null}
      </div>

      {clauses.length > 0 ? (
        <Accordion collapsible data-testid="how-determined">
          <AccordionItem value="how">
            <AccordionHeader>How this was determined</AccordionHeader>
            <AccordionPanel>
              {clauses.map(c => (
                <div key={c.clauseId} className={styles.clause}>
                  <Body1>{c.text}</Body1>
                  {c.formal ? <Caption1 className={styles.formal}>{c.formal}</Caption1> : null}
                </div>
              ))}
            </AccordionPanel>
          </AccordionItem>
        </Accordion>
      ) : null}

      {!closed && item.core && otherOpen.length > 0 ? (
        <div className={styles.section} data-testid="other-open-items">
          <Text weight="semibold">
            Other open {plan.lane} items{coreName ? ` on ${coreName}` : ''}{' '}
            <Badge appearance="tint" color="informative" size="small">
              {otherOpen.length}
            </Badge>
          </Text>
          <ul className={styles.otherList}>
            {otherOpen.map(o => {
              const l = composeIssueLine(o, today);
              return (
                <li key={o.signalId}>
                  <Body1>{l.headline}</Body1>
                  <Caption1 className={styles.meta}>
                    {' '}
                    {[l.ruleShortName, l.timing.text].filter(Boolean).join(' · ')}
                  </Caption1>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
};
