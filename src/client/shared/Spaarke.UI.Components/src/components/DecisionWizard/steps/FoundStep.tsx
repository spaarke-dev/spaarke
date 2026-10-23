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
});

export const FoundStep: React.FC = () => {
  const { item, plan, resolution, closedIds, today, openRecord } = useDecisionItem();
  // Items closed in this session (recorded here, or resolved by Also resolve) are no longer open.
  const otherOpen = item.otherOpenItems.filter(o => !closedIds.has(o.signalId));
  const styles = useStyles();
  const line = composeIssueLine(item.row, today);
  const closed = resolution != null;
  const evidence = resolution?.factSnapshot ?? item.evidence;
  const clauses = plan.ruleDescription?.clauses ?? [];
  const coreName = item.core?.name?.trim() || null;
  const coreLabel = item.core?.typeLabel?.trim() || 'record';
  const coreType = item.core?.recordType?.trim() || '';
  const coreId = item.core?.recordId?.trim() || '';
  // D-182: the subject is a link only when the host can open it and the subject has an entity and an id.
  const canOpenSubject = openRecord != null && coreName != null && coreType !== '' && coreId !== '';
  const openSubject = () => {
    if (!openRecord) return;
    // The record opens in a platform modal above the wizard; a dialog that fails to open changes nothing here.
    try {
      void Promise.resolve(openRecord(coreType, coreId)).catch(() => undefined);
    } catch {
      /* the wizard keeps its place */
    }
  };
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
          <Caption1 className={styles.meta}>No evidence was recorded for this item.</Caption1>
        ) : (
          evidence.map((e, i) => (
            <EvidenceLine
              key={`${e.kind}-${e.ref}-${i}`}
              tier={e.tier}
              text={e.text}
              subject={e.subject}
              withheld={e.withheld}
              source={e.source}
              asOf={e.asOf}
              clause={e.clause}
            />
          ))
        )}
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
