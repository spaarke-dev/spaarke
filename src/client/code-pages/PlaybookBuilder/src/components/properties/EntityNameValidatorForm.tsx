/**
 * EntityNameValidatorForm - Configuration form for Entity Name Validator (Tool) nodes.
 *
 * Scrubs LLM-emitted entity names that do not appear in a maker-supplied allow-list.
 * For each forbidden term found, the executor logs a `hallucination_detected`
 * observation event and either drops the surrounding sentence (default) or replaces
 * just the term with `[redacted]`. Backed by `EntityNameValidatorNodeExecutor`
 * (server, ActionType=141).
 *
 * Fields (matches server config contract — see EntityNameValidatorNodeExecutor):
 * - candidateText (required): Template expression resolving to the LLM-emitted text
 *   that should be scrubbed. Typically a previous-node output, e.g.
 *   `{{narrate.output.result}}`.
 * - allowList (required): Template expression resolving to the array of allowed
 *   entity names (e.g. matter names, contact names) loaded from a membership-scoped
 *   query upstream. Typically `{{names.output.result}}`.
 * - scrubStrategy (optional, default 'sentence'): How to handle a forbidden term:
 *     'sentence' — drop the entire sentence containing the term (preserves narrative
 *                  coherence; default)
 *     'phrase'   — replace the term itself with `[redacted]` (preserves sentence
 *                  but produces visibly redacted output)
 * - outputVariable (required): Canvas variable name where the scrubbed text is bound
 *   for downstream nodes.
 *
 * Validation: missing candidateText OR allowList OR outputVariable surfaces as a
 * config error in `node.data.validationErrors` (set by NodePropertiesForm's
 * NodeValidationBadge consumer).
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see spec FR-3 AC-3c - PlaybookBuilder properties form for EntityNameValidator
 * @see LookupUserMembershipForm - sibling form pattern (strict mirror per owner Q&A 2026-06-25)
 */

import { useCallback, useMemo, memo } from 'react';
import { makeStyles, tokens, Text, Input, Label, RadioGroup, Radio } from '@fluentui/react-components';
import type { RadioGroupOnChangeData } from '@fluentui/react-components';
import type { NodeFormProps } from '../../types/forms';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  intro: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Config shape — matches server EntityNameValidatorNodeExecutor contract
// ---------------------------------------------------------------------------

type ScrubStrategy = 'sentence' | 'phrase';

interface EntityNameValidatorConfig {
  /** Template expression resolving to the candidate LLM-emitted text to scrub. */
  candidateText: string;
  /** Template expression resolving to the allow-list (string[]) of permitted entity names. */
  allowList: string;
  /** Per-removal handling strategy. Default 'sentence' (drop sentence containing forbidden term). */
  scrubStrategy: ScrubStrategy;
  /** Canvas variable name to bind the scrubbed text to. */
  outputVariable: string;
}

const DEFAULT_CONFIG: EntityNameValidatorConfig = {
  candidateText: '',
  allowList: '',
  scrubStrategy: 'sentence',
  outputVariable: '',
};

// ---------------------------------------------------------------------------
// Helpers — parse/serialize
// ---------------------------------------------------------------------------

function parseConfig(json: string): EntityNameValidatorConfig {
  try {
    const parsed = JSON.parse(json) as Partial<EntityNameValidatorConfig>;
    const scrubStrategy: ScrubStrategy = parsed.scrubStrategy === 'phrase' ? 'phrase' : DEFAULT_CONFIG.scrubStrategy;
    return {
      candidateText: typeof parsed.candidateText === 'string' ? parsed.candidateText : DEFAULT_CONFIG.candidateText,
      allowList: typeof parsed.allowList === 'string' ? parsed.allowList : DEFAULT_CONFIG.allowList,
      scrubStrategy,
      outputVariable: typeof parsed.outputVariable === 'string' ? parsed.outputVariable : DEFAULT_CONFIG.outputVariable,
    };
  } catch {
    return { ...DEFAULT_CONFIG };
  }
}

function serializeConfig(config: EntityNameValidatorConfig): string {
  return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EntityNameValidatorForm = memo(function EntityNameValidatorForm({
  nodeId,
  configJson,
  onConfigChange,
}: NodeFormProps) {
  const styles = useStyles();
  const config = useMemo(() => parseConfig(configJson), [configJson]);

  const update = useCallback(
    (patch: Partial<EntityNameValidatorConfig>) => {
      onConfigChange(serializeConfig({ ...config, ...patch }));
    },
    [config, onConfigChange]
  );

  // -- Handlers --

  const handleCandidateTextChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ candidateText: e.target.value });
    },
    [update]
  );

  const handleAllowListChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ allowList: e.target.value });
    },
    [update]
  );

  const handleScrubStrategyChange = useCallback(
    (_e: React.FormEvent<HTMLDivElement>, data: RadioGroupOnChangeData) => {
      const next: ScrubStrategy = data.value === 'phrase' ? 'phrase' : 'sentence';
      update({ scrubStrategy: next });
    },
    [update]
  );

  const handleOutputVariableChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ outputVariable: e.target.value });
    },
    [update]
  );

  // -- Render --

  return (
    <div className={styles.form}>
      {/* Intro / behavior description */}
      <Text className={styles.intro}>
        Scrubs LLM-emitted entity names not present in the allow-list. Per removal, the executor logs a{' '}
        <code>hallucination_detected</code> observation event.
      </Text>

      {/* Candidate text source binding (required) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-candidateText`} size="small" required>
          Candidate text source binding
        </Label>
        <Input
          id={`${nodeId}-candidateText`}
          size="small"
          value={config.candidateText}
          onChange={handleCandidateTextChange}
          placeholder="e.g., {{narrate.output.result}}"
        />
        <Text className={styles.fieldHint}>
          Template expression resolving to the LLM-emitted text to scrub. Use the Template Variables panel to insert an
          upstream node output.
        </Text>
      </div>

      {/* Allow-list source binding (required) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-allowList`} size="small" required>
          Allow-list source binding
        </Label>
        <Input
          id={`${nodeId}-allowList`}
          size="small"
          value={config.allowList}
          onChange={handleAllowListChange}
          placeholder="e.g., {{names.output.result}}"
        />
        <Text className={styles.fieldHint}>
          Template expression resolving to a string array of permitted entity names (e.g., loaded from a
          membership-scoped query). Names not in this list are scrubbed from the candidate text.
        </Text>
      </div>

      {/* Scrub strategy (optional, default sentence) */}
      <div className={styles.field}>
        <Label id={`${nodeId}-scrubStrategy-label`} size="small">
          Scrub strategy
        </Label>
        <RadioGroup
          aria-labelledby={`${nodeId}-scrubStrategy-label`}
          value={config.scrubStrategy}
          onChange={handleScrubStrategyChange}
        >
          <Radio value="sentence" label="Sentence (drop whole sentence containing forbidden term — default)" />
          <Radio value="phrase" label="Phrase (replace forbidden term with [redacted])" />
        </RadioGroup>
        <Text className={styles.fieldHint}>
          Sentence-level removal preserves narrative coherence; phrase-level removal preserves sentence structure but
          produces visibly redacted output.
        </Text>
      </div>

      {/* Output Variable (required) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-outputVariable`} size="small" required>
          Output Variable
        </Label>
        <Input
          id={`${nodeId}-outputVariable`}
          size="small"
          value={config.outputVariable}
          onChange={handleOutputVariableChange}
          placeholder="e.g., scrubbedNarrative"
        />
        <Text className={styles.fieldHint}>
          Canvas variable name that downstream nodes reference, e.g. {'{{scrubbedNarrative.output.result}}'}.
        </Text>
      </div>
    </div>
  );
});
