using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;

namespace StockTrader.Services.ML;

public interface IMLModelTrainingService
{
    /// <summary>
    /// 백테스트 거래 내역 + SPY 데이터로 두 ML 모델을 학습합니다.
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
    private readonly ITradeRepository _tradeRepo;
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly MLSettings _mlSettings;
    private readonly ILogger<MLModelTrainingService> _logger;

    // 학습 진행 상태 (UI에 표시)
    private bool _isTraining;
    private string _trainingStatus = string.Empty;

    public MLModelTrainingService(
        IMarketRegimeClassifier regimeClassifier,
        ISignalScorer signalScorer,
        ITradeRepository tradeRepo,
        IDataFeedServiceFactory dataFeedFactory,
        IOptions<MLSettings> mlSettings,
        ILogger<MLModelTrainingService> logger)
    {
        _regimeClassifier = regimeClassifier;
        _signalScorer = signalScorer;
        _tradeRepo = tradeRepo;
        _dataFeedFactory = dataFeedFactory;
        _mlSettings = mlSettings.Value;
        _logger = logger;
    }

    public async Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default)
    {
        if (_isTraining)
        {
            return new MlTrainingResult
            {
                Success = false,
                Message = "이미 학습이 진행 중입니다. 잠시 후 다시 시도하세요."
            };
        }

        _isTraining = true;
        var startTime = DateTime.UtcNow;

        try
        {
            // ── Phase 1: 레짐 분류기 학습 ─────────────────────────────────
            _trainingStatus = "SPY 데이터 수집 중...";
            _logger.LogInformation("ML 학습 시작: 레짐 분류기");

            OhlcvBar[] spyBars = Array.Empty<OhlcvBar>();
            try
            {
                var dataFeed = await _dataFeedFactory.GetServiceAsync(ct);
                var spyFrom = DateTime.UtcNow.AddDays(-_mlSettings.RegimeTrainingDays);
                var spyList = await dataFeed.GetHistoricalBarsAsync("SPY", TimeFrame.Daily, spyFrom, DateTime.UtcNow, ct);
                spyBars = spyList.ToArray();
                _logger.LogInformation("SPY 데이터 수집 완료: {Count}개 바", spyBars.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SPY 데이터 수집 실패 — 레짐 분류기 학습 건너뜀");
            }

            bool regimeTrained = false;
            int regimeSamples = 0;

            if (spyBars.Length >= _mlSettings.MinTrainingSamples)
            {
                _trainingStatus = "시장 레짐 분류기 학습 중...";
                regimeTrained = await _regimeClassifier.TrainAsync(spyBars, ct);
                regimeSamples = spyBars.Length;
            }
            else
            {
                _logger.LogWarning("SPY 데이터 부족으로 레짐 분류기 학습 건너뜀 ({Count}개)", spyBars.Length);
            }

            // ── Phase 2: 시그널 스코어러 학습 ────────────────────────────
            _trainingStatus = "거래 내역 로딩 중...";
            _logger.LogInformation("ML 학습: 시그널 스코어러");

            var trades = await _tradeRepo.GetRecentAsync(limit: 5000, ct: ct);

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

            var duration = DateTime.UtcNow - startTime;
            _trainingStatus = "완료";

            var anySuccess = regimeTrained || scorerTrained;

            var message = BuildResultMessage(regimeTrained, scorerTrained, spyBars.Length, trades.Count);

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
            _isTraining = false;
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
        IsTraining = _isTraining,
        TrainingStatus = _trainingStatus
    };

    private static string BuildResultMessage(bool regimeTrained, bool scorerTrained, int spySamples, int tradeSamples)
    {
        var parts = new List<string>();

        if (regimeTrained)
            parts.Add($"레짐 분류기 학습 완료 ({spySamples}개 SPY 바)");
        else
            parts.Add("레짐 분류기 건너뜀 (데이터 부족)");

        if (scorerTrained)
            parts.Add($"시그널 스코어러 학습 완료 ({tradeSamples}개 거래)");
        else
            parts.Add($"시그널 스코어러 건너뜀 (최소 {50}개 거래 필요, 현재 {tradeSamples}개)");

        return string.Join(" | ", parts);
    }
}
