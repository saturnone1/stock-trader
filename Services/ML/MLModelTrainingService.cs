using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
// IServiceScopeFactory: Singleton에서 Scoped 의존성 안전 접근

namespace StockTrader.Services.ML;

public interface IMLModelTrainingService
{
    /// <summary>
    /// 백테스트 거래 내역 + 공급자 기준 종목 데이터로 두 ML 모델을 학습합니다.
    /// UI의 "모델 학습" 버튼에서 직접 호출됩니다.
    /// </summary>
    Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default);

    /// <summary>현재 ML 모델 상태 조회</summary>
    MlModelStatus GetStatus();
}

/// <summary>
/// ML 모델 학습 오케스트레이터.
/// 데이터 수집 → 피처 생성 → 학습 → 저장 흐름을 조율합니다.
/// </summary>
public class MLModelTrainingService : IMLModelTrainingService
{
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly ISignalScorer _signalScorer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MLSettings _mlSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MLModelTrainingService> _logger;

    // 학습 진행 상태 (UI에 표시, Interlocked으로 동시 학습 방지)
    private int _isTraining; // 0=idle, 1=training
    private volatile string _trainingStatus = string.Empty;

    public MLModelTrainingService(
        IMarketRegimeClassifier regimeClassifier,
        ISignalScorer signalScorer,
        IServiceScopeFactory scopeFactory,
        IOptions<MLSettings> mlSettings,
        TimeProvider timeProvider,
        ILogger<MLModelTrainingService> logger)
    {
        _regimeClassifier = regimeClassifier;
        _signalScorer = signalScorer;
        _scopeFactory = scopeFactory;
        _mlSettings = mlSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isTraining, 1, 0) != 0)
        {
            return new MlTrainingResult
            {
                Success = false,
                Message = "이미 학습이 진행 중입니다. 잠시 후 다시 시도하세요."
            };
        }
        var startTime = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            // ── Phase 1: 레짐 분류기 학습 ─────────────────────────────────
            _logger.LogInformation("ML 학습 시작: 레짐 분류기");

            var regimeSymbol = DataProviderCatalog.UnitedStatesRegimeBenchmark;
            OhlcvBar[] regimeBars = [];
            try
            {
                using var dataScope = _scopeFactory.CreateScope();
                var dataFeedFactory = dataScope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
                var feedSelection = await dataFeedFactory.SelectAsync(null, ct);
                regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
                _trainingStatus = $"{regimeSymbol} 데이터 수집 중...";
                var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
                var regimeFrom = observedAt.AddDays(-_mlSettings.RegimeTrainingDays);
                var regimeList = await feedSelection.Service.GetHistoricalBarsAsync(
                    regimeSymbol, TimeFrame.Daily, regimeFrom, observedAt, ct);
                regimeBars = regimeList.ToArray();
                _logger.LogInformation(
                    "{Symbol} 데이터 수집 완료: {Count}개 바",
                    regimeSymbol,
                    regimeBars.Length);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Symbol} 데이터 수집 실패 — 레짐 분류기 학습 건너뜀",
                    regimeSymbol);
            }

            bool regimeTrained = false;
            int regimeSamples = 0;

            if (regimeBars.Length >= _mlSettings.MinTrainingSamples)
            {
                _trainingStatus = "시장 레짐 분류기 학습 중...";
                regimeTrained = await _regimeClassifier.TrainAsync(regimeBars, ct);
                regimeSamples = regimeBars.Length;
            }
            else
            {
                _logger.LogWarning(
                    "{Symbol} 데이터 부족으로 레짐 분류기 학습 건너뜀 ({Count}개)",
                    regimeSymbol,
                    regimeBars.Length);
            }

            // ── Phase 2: 시그널 스코어러 학습 ────────────────────────────
            _trainingStatus = "거래 내역 로딩 중...";
            _logger.LogInformation("ML 학습: 시그널 스코어러");

            using var tradeScope = _scopeFactory.CreateScope();
            var tradeRepo = tradeScope.ServiceProvider.GetRequiredService<ITradeRepository>();
            var trades = await tradeRepo.GetRecentAsync(limit: 5000, ct: ct);

            bool scorerTrained = false;
            double accuracy = 0;
            double auc = 0;

            if (trades.Count >= _mlSettings.MinTrainingSamples)
            {
                _trainingStatus = $"시그널 스코어러 학습 중... ({trades.Count}개 거래 내역)";
                scorerTrained = await _signalScorer.TrainAsync(trades, ct);

                if (scorerTrained)
                {
                    accuracy = _signalScorer.LastAccuracy;
                    auc = _signalScorer.LastAuc;
                }
            }
            else
            {
                _logger.LogWarning("거래 내역 부족으로 시그널 스코어러 학습 건너뜀 ({Count}개)", trades.Count);
            }

            var duration = _timeProvider.GetUtcNow().UtcDateTime - startTime;
            _trainingStatus = "완료";

            var anySuccess = regimeTrained || scorerTrained;

            var message = BuildResultMessage(
                regimeTrained,
                scorerTrained,
                regimeSymbol,
                regimeBars.Length,
                trades.Count,
                _mlSettings.MinTrainingSamples);

            _logger.LogInformation("ML 학습 완료: {Duration:F1}초, 레짐={Regime}, 스코어러={Scorer}",
                duration.TotalSeconds, regimeTrained, scorerTrained);

            return new MlTrainingResult
            {
                Success = anySuccess,
                Message = message,
                RegimeSamples = regimeSamples,
                SignalSamples = trades.Count,
                SignalScorerAccuracy = accuracy,
                SignalScorerAuc = auc,
                TrainingDuration = duration
            };
        }
        catch (OperationCanceledException)
        {
            _trainingStatus = "취소됨";
            return new MlTrainingResult { Success = false, Message = "학습이 취소되었습니다." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML 학습 중 예상치 못한 오류 발생");
            _trainingStatus = "오류 발생";
            return new MlTrainingResult
            {
                Success = false,
                Message = $"학습 중 오류 발생: {ex.Message}"
            };
        }
        finally
        {
            Interlocked.Exchange(ref _isTraining, 0);
        }
    }

    public MlModelStatus GetStatus() => new()
    {
        IsRegimeModelLoaded = _regimeClassifier.IsModelLoaded,
        IsSignalScorerLoaded = _signalScorer.IsModelLoaded,
        RegimeModelTrainedAt = _regimeClassifier.TrainedAt,
        SignalScorerTrainedAt = _signalScorer.TrainedAt,
        SignalScorerAccuracy = _signalScorer.LastAccuracy,
        RegimeTrainingSamples = _regimeClassifier.TrainingSamples,
        SignalScorerTrainingSamples = _signalScorer.TrainingSamples,
        SignalScorerFeatureImportances = _signalScorer.FeatureImportances,
        IsTraining = Volatile.Read(ref _isTraining) != 0,
        TrainingStatus = _trainingStatus
    };

    private static string BuildResultMessage(
        bool regimeTrained,
        bool scorerTrained,
        string regimeSymbol,
        int regimeSamples,
        int tradeSamples,
        int minimumTrainingSamples)
    {
        var parts = new List<string>();

        if (regimeTrained)
            parts.Add($"레짐 분류기 학습 완료 ({regimeSamples}개 {regimeSymbol} 바)");
        else
            parts.Add("레짐 분류기 건너뜀 (데이터 부족)");

        if (scorerTrained)
            parts.Add($"시그널 스코어러 학습 완료 ({tradeSamples}개 거래)");
        else
            parts.Add($"시그널 스코어러 건너뜀 (최소 {minimumTrainingSamples}개 거래 필요, 현재 {tradeSamples}개)");

        return string.Join(" | ", parts);
    }
}
