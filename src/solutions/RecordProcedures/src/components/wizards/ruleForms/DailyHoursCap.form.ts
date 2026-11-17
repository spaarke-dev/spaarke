import type { RuleFormDef } from './types';

export const dailyHoursCapForm: RuleFormDef = {
  ruleType: 'DailyHoursCap',
  label: 'Daily hours cap',
  fields: [
    { key: 'maxHoursPerDay', kind: 'number', label: "Most hours per day", help: "The most hours one timekeeper may bill on one day. More than 0, at most 24.", required: true, min: 0, minExclusive: true, max: 24 },
  ],
};
