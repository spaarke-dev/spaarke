export interface IPortfolioHealth {
  totalSpend: number;
  totalBudget: number;
  utilizationPercent: number;
  mattersAtRisk: number;
  overdueEvents: number;
  activeMatters: number;
}

export interface IHealthMetrics {
  mattersAtRisk: number;
  overdueEvents: number;
  activeMatters: number;
  budgetUtilizationPercent: number;
  portfolioSpend: number;
  portfolioBudget: number;
}

export interface IQuickSummary {
  activeCount: number;
  spendFormatted: string;
  budgetFormatted: string;
  atRiskCount: number;
  overdueCount: number;
  topPriorityMatter?: string;
  briefingText?: string;
}
