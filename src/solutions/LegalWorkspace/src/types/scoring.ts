import { PriorityLevel, EffortLevel } from './enums';

export interface IPriorityFactor {
  factorName: string;
  points: number;
  explanation: string;
}

export interface IPriorityScore {
  score: number;          // 0-100, capped
  level: PriorityLevel;   // Urgent/High/Normal/Low
  factors: IPriorityFactor[];
  reasonString: string;
}

export interface IEffortMultiplier {
  name: string;
  value: number;
}

export interface IEffortScore {
  score: number;          // 0-100, capped
  level: EffortLevel;     // High/Med/Low
  baseEffort: number;
  eventType: string;
  appliedMultipliers: IEffortMultiplier[];
  reasonString: string;
}

export interface IScoreResult {
  priority: IPriorityScore;
  effort: IEffortScore;
}
