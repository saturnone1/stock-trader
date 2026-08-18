import { api } from './client';
import type { DashboardData, Pattern, CustomPatternDocument, CustomPatternWriteRequest, OptimizationJob, BacktestResult, AuthSession, UniverseMeta, UniverseQueryResult, FinancialFactorMeta, FinancialFactorQueryResult, FinancialPipelineStatus, SettingsResponse, SettingsUpdateRequest, SettingsUpdateResponse } from './types';

const CUMULATIVE_RSI_PRESET_ID = -1001
const CUMULATIVE_RSI_PRESET_NAME = '누적 RSI 절대수익'

function buildCumulativeRsiPreset(documentVersion: number): CustomPatternDocument {
  const timestamp = new Date().toISOString()
  const entryGroups = [
    {
      label: '과매도 추세 진입',
      logic: 'AND',
      rules: [
        {
          indicator: 'CUMULATIVE_RSI',
          operator: '<=',
          value: 10,
          withinBars: 0,
          consecutiveBars: 0,
          refSymbol: '',
          compareIndicator: '',
          compareParams: {},
          weight: 1,
          params: {
            period: 2,
            cumulativePeriod: 2
          }
        },
        {
          indicator: 'PRICE_VS_SMA',
          operator: '>=',
          value: 0,
          withinBars: 0,
          consecutiveBars: 0,
          refSymbol: '',
          compareIndicator: '',
          compareParams: {},
          weight: 1,
          params: {
            period: 200
          }
        }
      ]
    }
  ]

  const exitRules = [
    {
      indicator: 'CUMULATIVE_RSI',
      operator: '>=',
      value: 65,
      withinBars: 0,
      consecutiveBars: 0,
      refSymbol: '',
      compareIndicator: '',
      compareParams: {},
      weight: 1,
      params: {
        period: 2,
        cumulativePeriod: 2
      }
    },
    {
      indicator: 'PRICE_VS_SMA',
      operator: '<',
      value: 0,
      withinBars: 0,
      consecutiveBars: 0,
      refSymbol: '',
      compareIndicator: '',
      compareParams: {},
      weight: 1,
      params: {
        period: 200
      }
    }
  ]

  return {
    id: CUMULATIVE_RSI_PRESET_ID,
    documentVersion,
    name: CUMULATIVE_RSI_PRESET_NAME,
    description: '사이트에 소개된 누적 RSI 절대수익 전략. 2일 누적 RSI(2) 10 이하 + 200일선 위에서 진입하고, 65 이상 또는 200일선 이탈 시 청산합니다.',
    entryRulesJson: '[]',
    entryLogic: 'AND',
    requireBullRegime: false,
    atrStopMultiplier: 5,
    atrTargetMultiplier: 10,
    maxHoldingBars: 60,
    trailingAtr: 0,
    partialProfitR: 0,
    useWeightTiers: false,
    weightTiersJson: '[]',
    defaultAllocationPercent: 100,
    exitRulesJson: JSON.stringify(exitRules),
    exitRulesLogic: 'OR',
    exitGroupsJson: '[]',
    exitGroupsLogic: 'OR',
    scalingRulesJson: '[]',
    timeFilterJson: '{}',
    circuitBreakerJson: '{}',
    reentryJson: '{}',
    portfolioRulesJson: '{}',
    entryGroupsJson: JSON.stringify(entryGroups),
    entryGroupsLogic: 'AND',
    dynamicExitJson: '{}',
    entryMode: 'CurrentClose',
    timeFrame: 'Daily',
    sizingMode: 'FixedRisk',
    isActive: true,
    enableLiveTrading: false,
    createdAt: timestamp,
    updatedAt: timestamp
  }
}

function withPresetPatterns(patterns: CustomPatternDocument[], documentVersion: number) {
  if (patterns.some((pattern) => pattern?.name === CUMULATIVE_RSI_PRESET_NAME)) {
    return patterns
  }

  return [buildCumulativeRsiPreset(documentVersion), ...patterns]
}

function isPresetPatternId(id: string) {
  return String(id) === String(CUMULATIVE_RSI_PRESET_ID)
}

function parsePatternRules(pattern: CustomPatternDocument) {
  try {
    const directRules = JSON.parse(pattern.entryRulesJson || '[]');
    if (Array.isArray(directRules) && directRules.length > 0) {
      return directRules;
    }
  } catch {
    // fall through to grouped rules
  }

  try {
    const groups = JSON.parse(pattern.entryGroupsJson || '[]');
    if (Array.isArray(groups)) {
      return groups.flatMap((group: any) => group.rules ?? group.Rules ?? []);
    }
  } catch {
    // ignore malformed grouped rules
  }

  return [];
}

function normalizePattern(pattern: CustomPatternDocument): Pattern {
  return {
    id: String(pattern.id),
    name: pattern.name,
    description: pattern.description || '',
    rules: parsePatternRules(pattern),
    createdAt: pattern.createdAt,
    updatedAt: pattern.updatedAt,
    raw: pattern
  };
}

export const authApi = {
  me: () => api.get<AuthSession>('/api/auth/me'),
  bootstrap: () => api.get('/api/auth/bootstrap'),
  login: (username: string, password: string) =>
    api.post('/api/auth/login', { username, password }),
  register: (username: string, password: string) =>
    api.post('/api/auth/register', { username, password }),
  logout: () => api.post('/api/auth/logout', {}),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post('/api/auth/change-password', { currentPassword, newPassword }),
};

export const dashboardApi = {
  get: async () => {
    const response = await api.get('/api/dashboard');
    const data = response.data ?? {};
    const positions = Array.isArray(data.positions) ? data.positions : [];

    return {
      ...response,
      data: {
        accountInfo: data.account ? {
          accountId: data.account.accountId,
          balance: data.account.cash ?? 0,
          availableBalance: data.account.buyingPower ?? 0,
          equity: data.account.totalEquity ?? 0,
        } : undefined,
        riskState: data.riskState ? {
          totalExposure: 0,
          maxDrawdown: Math.max(0, -(data.riskState.dailyPnLPercent ?? 0)),
          riskLevel: data.riskState.isTradingHalted ? 'HIGH' : 'LOW',
        } : undefined,
        positions: positions.map((pos: any) => ({
          symbol: pos.symbol,
          quantity: pos.quantity ?? 0,
          avgPrice: pos.entryPrice ?? 0,
          currentPrice: pos.currentPrice ?? 0,
          pnl: pos.unrealizedPnL ?? 0,
          pnlPercent: pos.entryPrice && pos.quantity
            ? (pos.unrealizedPnL ?? 0) / (pos.entryPrice * pos.quantity)
            : 0,
          orderStatus: pos.orderStatus ?? 'Ready',
          orderRequestedAt: pos.orderRequestedAt ?? null,
          orderReason: pos.orderReason ?? null,
          orderKind: pos.orderKind ?? null,
          hasBrokerOrderId: !!pos.hasBrokerOrderId,
          orderPendingSeconds: pos.orderPendingSeconds ?? 0,
          orderQuantity: pos.orderQuantity ?? 0,
          orderMarksPartialProfit: pos.orderMarksPartialProfit ?? false,
        })),
        signals: data.recentSignals ?? [],
        recommendations: data.recentSignals ?? [],
        marketRegime: data.marketRegime ?? 'Unknown',
      } as DashboardData
    };
  },
};

export const patternApi = {
  list: async () => {
    const [response, metadata] = await Promise.all([
      api.get<CustomPatternDocument[]>('/api/custom-patterns'),
      metadataApi.getStrategyBuilder()
    ])
    const patterns = withPresetPatterns(response.data ?? [], metadata.documentVersion)
    return {
      ...response,
      data: patterns.map(normalizePattern)
    };
  },
  get: async (id: string) => {
    if (isPresetPatternId(id)) {
      const metadata = await metadataApi.getStrategyBuilder()
      return {
        data: normalizePattern(buildCumulativeRsiPreset(metadata.documentVersion))
      };
    }
    const response = await api.get<CustomPatternDocument>(`/api/custom-patterns/${id}`);
    return {
      ...response,
      data: normalizePattern(response.data)
    };
  },
  create: async (data: { name: string; description?: string }) => {
    const starterGroup = [{
      label: '매수 상황 1',
      logic: 'AND',
      rules: [{ indicator: 'RSI', params: { period: 14 }, operator: '<=', value: 30, withinBars: 0, consecutiveBars: 0, refSymbol: '', compareIndicator: '', compareParams: {}, weight: 1 }]
    }]
    const response = await api.post<CustomPatternDocument>('/api/custom-patterns', {
      name: data.name,
      description: data.description || '',
      entryRulesJson: '[]',
      entryLogic: 'AND',
      entryGroupsJson: JSON.stringify(starterGroup),
      entryGroupsLogic: 'AND',
      isActive: true
    });
    return {
      ...response,
      data: normalizePattern(response.data)
    };
  },
  update: async (id: string, data: CustomPatternWriteRequest) => {
    const response = isPresetPatternId(id)
      ? await api.post<CustomPatternDocument>('/api/custom-patterns', data)
      : await api.put<CustomPatternDocument>(`/api/custom-patterns/${id}`, data);
    return {
      ...response,
      data: normalizePattern(response.data)
    };
  },
  delete: (id: string) => isPresetPatternId(id)
    ? Promise.resolve({ data: null })
    : api.delete(`/api/custom-patterns/${id}`),
  preview: (symbol: string, pattern: CustomPatternWriteRequest, options: { timeFrame?: string; from?: string; to?: string } = {}) =>
    api.post('/api/custom-patterns/preview', { symbol, pattern, ...options }),
};

let strategyBuilderMetadataPromise: Promise<any> | null = null;

export const metadataApi = {
  getStrategyBuilder: () => {
    if (!strategyBuilderMetadataPromise) {
      strategyBuilderMetadataPromise = api.get('/api/metadata/strategy-builder')
        .then(response => response.data)
        .catch(error => {
          strategyBuilderMetadataPromise = null;
          throw error;
        });
    }
    return strategyBuilderMetadataPromise;
  },
};

export const optimizationApi = {
  normalizeResult: (result: any) => ({
    id: result.id,
    rank: result.rank,
    params: result.params ?? {},
    sharpeRatio: result.sharpeRatio ?? 0,
    sortinoRatio: result.sortinoRatio ?? 0,
    totalReturn: result.totalReturn ?? 0,
    maxDrawdown: result.maxDrawdown ?? 0,
    winRate: result.winRate ?? 0,
    tradeCount: result.totalTrades ?? result.tradeCount ?? 0,
    profitFactor: result.profitFactor ?? 0,
    calmarRatio: result.calmarRatio ?? 0,
    annualizedReturn: result.annualizedReturn ?? 0,
    oosTotalReturn: result.oosTotalReturn,
    oosSortinoRatio: result.oosSortinoRatio,
    oosSharpeRatio: result.oosSharpeRatio,
    oosMaxDrawdown: result.oosMaxDrawdown,
    oosWinRate: result.oosWinRate,
    oosTotalTrades: result.oosTotalTrades,
    oosProfitFactor: result.oosProfitFactor,
    oosCalmarRatio: result.oosCalmarRatio,
    oosAnnualizedReturn: result.oosAnnualizedReturn,
  }),
  normalizeJob: (job: any) => ({
    id: String(job.id),
    name: job.name,
    status: job.status,
    priority: job.priority ?? 0,
    progress: job.progressPercent ?? job.progress ?? 0,
    completedCombinations: job.testedCombinations ?? job.completedCombinations ?? 0,
    totalCombinations: job.totalCombinations ?? 0,
    createdAt: job.createdAt,
    startedAt: job.startedAt,
    completedAt: job.completedAt,
    elapsedSeconds: job.elapsedSeconds,
    estimatedRemainingSeconds: job.estimatedRemainingSeconds,
    lastProgressAt: job.lastProgressAt,
    errorMessage: job.errorMessage,
    continuousMode: !!job.continuousMode,
    autoApplyBestResult: !!job.autoApplyBestResult,
    autoApplyMinTrades: job.autoApplyMinTrades ?? 0,
    appliedResultCount: job.appliedResultCount ?? 0,
    lastAutoAppliedAt: job.lastAutoAppliedAt,
    lastAutoAppliedResultId: job.lastAutoAppliedResultId,
    lastAutoApplyMessage: job.lastAutoApplyMessage,
    topResults: job.topResults?.map((result: any) => optimizationApi.normalizeResult(result)) ?? []
  }),
  list: async () => {
    const response = await api.get('/api/optimize-jobs');
    return {
      ...response,
      data: (response.data ?? []).map((job: any) => optimizationApi.normalizeJob(job)) as OptimizationJob[]
    };
  },
  get: async (id: string) => {
    const response = await api.get<OptimizationJob>(`/api/optimize-jobs/${id}`);
    return {
      ...response,
      data: optimizationApi.normalizeJob(response.data)
    };
  },
  create: (data: any) => api.post<OptimizationJob>('/api/optimize-jobs', data),
  cancel: (id: string) => api.post(`/api/optimize-jobs/${id}/cancel`, {}),
  pause: (id: string) => api.post(`/api/optimize-jobs/${id}/pause`, {}),
  resume: (id: string) => api.post(`/api/optimize-jobs/${id}/resume`, {}),
  remove: (id: string) => api.delete(`/api/optimize-jobs/${id}`),
  results: async (id: string, top = 50) => {
    const response = await api.get(`/api/optimize-jobs/${id}/results`, { params: { top } });
    return {
      ...response,
      data: (response.data ?? []).map((result: any) => optimizationApi.normalizeResult(result))
    };
  },
  updateSettings: (id: string, data: any) => api.post(`/api/optimize-jobs/${id}/settings`, data),
  applyResult: (id: string, resultId?: number | null) => api.post(`/api/optimize-jobs/${id}/apply-result`, { resultId: resultId ?? null }),
};

export const backtestApi = {
  list: async () => ({ data: [] as BacktestResult[] }),
  get: async () => ({ data: null }),
  start: (data: any) => api.post('/api/backtest', data),
};

export const tradeApi = {
  recommendations: () => api.get('/api/trades/recommendations'),
  positions: () => api.get('/api/trades/positions'),
  history: (params: Record<string, string | number | undefined> = {}) =>
    api.get('/api/trades/history', { params }),
};

export const analysisApi = {
  get: (symbol: string) => api.get(`/api/analysis/${encodeURIComponent(symbol)}`),
};

export const signalApi = {
  list: (params: Record<string, string | number | undefined> = {}) =>
    api.get('/api/signals', { params }),
};

export const riskApi = {
  get: () => api.get('/api/risk'),
};

export const portfolioApi = {
  get: () => api.get('/api/portfolio'),
  performance: () => api.get('/api/portfolio/performance'),
};

export const settingsApi = {
  get: () => api.get<SettingsResponse>('/api/settings'),
  update: (data: SettingsUpdateRequest) => api.put<SettingsUpdateResponse>('/api/settings', data),
};

export const accountApi = {
  metadata: () => api.get('/api/accounts/meta'),
  list: () => api.get('/api/accounts'),
  create: (data: any) => api.post('/api/accounts', data),
  update: (id: number | string, data: any) => api.put(`/api/accounts/${id}`, data),
  remove: (id: number | string) => api.delete(`/api/accounts/${id}`),
  test: (id: number | string) => api.post(`/api/accounts/${id}/test`, {}),
  activate: (id: number | string) => api.post(`/api/accounts/${id}/activate`, {}),
};

export const mlApi = {
  status: () => api.get('/api/ml'),
  train: () => api.post('/api/ml/train', {}),
};

export const patternStatsApi = {
  list: () => api.get('/api/pattern-stats'),
};

export const universeApi = {
  meta: () => api.get<UniverseMeta>('/api/universe/meta'),
  query: async (params: Record<string, string | number | undefined>) => {
    const response = await api.get<UniverseQueryResult>('/api/universe/query', { params });
    return {
      ...response,
      data: {
        totalUniverse: response.data?.totalUniverse ?? 0,
        matched: response.data?.matched ?? 0,
        items: response.data?.items ?? []
      }
    };
  }
};

export const financialFactorApi = {
  meta: () => api.get<FinancialFactorMeta>('/api/financial-factors/meta'),
  query: async (params: Record<string, string | number | boolean | undefined>) => {
    const response = await api.get<FinancialFactorQueryResult>('/api/financial-factors/query', { params });
    return {
      ...response,
      data: {
        totalUniverse: response.data?.totalUniverse ?? 0,
        matched: response.data?.matched ?? 0,
        items: response.data?.items ?? [],
        comparison: response.data?.comparison ?? {
          overall: { count: 0, positiveEarningsCount: 0, turnaroundCount: 0 },
          filtered: { count: 0, positiveEarningsCount: 0, turnaroundCount: 0 }
        }
      }
    };
  },
  import: (items: any[]) => api.post('/api/financial-factors/import', items),
  pipelineStatus: async () => {
    const response = await api.get<FinancialPipelineStatus>('/api/financial-factors/pipeline/status');
    return {
      ...response,
      data: {
        enabled: !!response.data?.enabled,
        importDirectory: response.data?.importDirectory ?? '',
        scanIntervalMinutes: response.data?.scanIntervalMinutes ?? 0,
        latestSuccessAt: response.data?.latestSuccessAt ?? null,
        vendorSync: response.data?.vendorSync ?? {
          enabled: false,
          provider: 'SEC',
          syncIntervalHours: 24,
          symbolLimit: 50,
          configuredSymbolCount: 0,
          configuredSymbols: [],
          latestSuccessAt: null
        },
        recentRuns: response.data?.recentRuns ?? []
      }
    };
  },
  runPipeline: () => api.post('/api/financial-factors/pipeline/run', {}),
  runVendorSync: (symbols?: string) => api.post('/api/financial-factors/vendor-sync/run', { symbols })
};

export const orderApi = {
  executeSignal: (signalId: number | string) =>
    api.post('/api/orders/execute-signal', { signalId: Number(signalId) }),
  closePosition: (symbol: string) =>
    api.post('/api/orders/close-position', { symbol }),
  reconcilePositionOrder: (symbol: string) =>
    api.post('/api/orders/reconcile-position-order', { symbol }),
};
