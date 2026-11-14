import type { RuleFormDef } from './types';

export const aiObservationForm: RuleFormDef = {
  ruleType: 'AiObservation',
  label: 'AI observation',
  fields: [
    { key: 'observation', kind: 'choice', label: "Observation", help: "The kind of observation the AI looks for.", required: true, options: [{ value: 'blockBilling', label: "Block billing" }, { value: 'vagueDescription', label: "Vague description" }] },
    { key: 'minConfidence', kind: 'number', label: "Minimum confidence", help: "Confidence at or above which the observation counts. More than 0, at most 1.", required: true, min: 0, minExclusive: true, max: 1 },
  ],
};
