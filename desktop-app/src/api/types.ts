import type { components } from './generated';

export interface AuthSession {
  userId: string;
  username: string;
  authenticated: boolean;
}

export interface UniverseMeta {
  totalActive: number;
  marketCapCoverage: number;
  sectors: Array<{ name: string; count: number }>;
  industries: Array<{ name: string; count: number }>;
}

export interface UniverseItem {
  symbol: string;
  name: string;
  sector: string;
  industry: string;
  marketCap: number;
  marketCapPercentile: number;
}

export interface UniverseQueryResult {
  totalUniverse: number;
  matched: number;
  items: UniverseItem[];
}

export interface FinancialFactorMeta {
  totalSnapshots: number;
  symbolsCovered: number;
  latestAsOfDate?: string | null;
  coverage: {
    peRatio: number;
    pbRatio: number;
    roePercent: number;
    revenueGrowth: number;
    netIncomeGrowth: number;
    turnaround: number;
  };
}

export interface FinancialFactorItem {
  symbol: string;
  name: string;
  sector: string;
  industry: string;
  marketCap?: number | null;
  asOfDate: string;
  peRatio?: number | null;
  pbRatio?: number | null;
  roePercent?: number | null;
  operatingMarginPercent?: number | null;
  revenueGrowthYoY?: number | null;
  netIncomeGrowthYoY?: number | null;
  hasPositiveEarnings: boolean;
  isTurnaround: boolean;
  source: string;
}

export interface FinancialFactorSummary {
  count: number;
  averagePe?: number | null;
  averagePb?: number | null;
  averageRoe?: number | null;
  averageRevenueGrowth?: number | null;
  averageNetIncomeGrowth?: number | null;
  positiveEarningsCount: number;
  turnaroundCount: number;
}

export interface FinancialFactorQueryResult {
  totalUniverse: number;
  matched: number;
  items: FinancialFactorItem[];
  comparison: {
    overall: FinancialFactorSummary;
    filtered: FinancialFactorSummary;
  };
}

export interface FinancialPipelineRun {
  id: number;
  sourceType: string;
  filePath: string;
  status: string;
  importedCount: number;
  skippedCount: number;
  errorMessage?: string | null;
  startedAt: string;
  completedAt?: string | null;
}

export interface FinancialVendorSyncStatus {
  enabled: boolean;
  provider: string;
  syncIntervalHours: number;
  symbolLimit: number;
  configuredSymbolCount: number;
  configuredSymbols: string[];
  latestSuccessAt?: string | null;
}

export interface FinancialPipelineStatus {
  enabled: boolean;
  importDirectory: string;
  scanIntervalMinutes: number;
  latestSuccessAt?: string | null;
  vendorSync: FinancialVendorSyncStatus;
  recentRuns: FinancialPipelineRun[];
}

// Patterns. The server contract is generated from ASP.NET Core OpenAPI metadata.
export type CustomPatternDocument = components['schemas']['CustomPatternResponse'];
export type CustomPatternWriteRequest = Required<
  components['schemas']['CustomPatternWriteRequest']
>;
export type StrategyDocument = components['schemas']['StrategyDocument'];
export type SettingsResponse = components['schemas']['SettingsResponse'];
export type SettingsUpdateRequest = components['schemas']['SettingsUpdateRequest'];
export type SettingsUpdateResponse = components['schemas']['SettingsUpdateResponse'];

export interface Pattern {
  id: string;
  name: string;
  description: string;
  rules?: PatternRule[];
  createdAt: string;
  updatedAt: string;
  raw?: CustomPatternDocument;
}

export interface PatternRule {
  id: string;
  type: string;
  condition: string;
}

// Optimization Jobs
export interface OptimizationJob {
  id: string;
  name: string;
    status: 'Pending' | 'Running' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled';
  priority: number;
  progress: number;
  completedCombinations?: number;
  totalCombinations?: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  topResults?: OptimizationResult[];
}

export interface OptimizationResult {
    id: number;
    rank: number;
  sharpeRatio: number;
  sortinoRatio: number;
  totalReturn: number;
  maxDrawdown: number;
  winRate: number;
  tradeCount: number;
}
