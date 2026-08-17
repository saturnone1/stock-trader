// Dashboard
export interface DashboardData {
  accountInfo?: {
    accountId: string;
    balance: number;
    availableBalance: number;
    equity: number;
  };
  riskState?: {
    totalExposure: number;
    maxDrawdown: number;
    riskLevel: string;
  };
  positions?: Position[];
  signals?: Signal[];
  recommendations?: Recommendation[];
  marketRegime?: string;
}

export interface AuthSession {
  userId: string;
  username: string;
  authenticated: boolean;
}

export interface Position {
  symbol: string;
  quantity: number;
  avgPrice: number;
  currentPrice: number;
  pnl: number;
  pnlPercent: number;
  exitStatus?: 'Ready' | 'SubmissionUnconfirmed' | 'AwaitingBroker';
  exitRequestedAt?: string | null;
  exitRequestReason?: string | null;
  hasExitOrderId?: boolean;
  exitPendingSeconds?: number;
}

export interface Signal {
  id: string;
  symbol: string;
  type: 'BUY' | 'SELL';
  strength: number;
  timestamp: string;
}

export interface Recommendation {
  id: string;
  symbol: string;
  action: string;
  score: number;
  reason: string;
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

// Patterns
export interface Pattern {
  id: string;
  name: string;
  description: string;
  rules?: PatternRule[];
  createdAt: string;
  updatedAt: string;
  raw?: any;
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
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
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
  rank: number;
  sharpeRatio: number;
  sortinoRatio: number;
  totalReturn: number;
  maxDrawdown: number;
  winRate: number;
  tradeCount: number;
}

// Backtest
export interface BacktestResult {
  id: string;
  patternId: string;
  symbol: string;
  status: 'Running' | 'Completed' | 'Failed';
  startDate: string;
  endDate: string;
  metrics?: BacktestMetrics;
}

export interface BacktestMetrics {
  totalReturn: number;
  sharpeRatio: number;
  sortinoRatio: number;
  maxDrawdown: number;
  winRate: number;
  profitFactor: number;
  tradeCount: number;
}
