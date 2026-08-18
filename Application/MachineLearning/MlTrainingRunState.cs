namespace StockTrader.Application.MachineLearning;

/// <summary>Scoped 학습 use case 사이에서 하나의 전역 실행 claim과 표시 상태를 공유합니다.</summary>
internal sealed class MlTrainingRunState
{
    private int _isTraining;
    private string _status = string.Empty;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref _isTraining, 1, 0) != 0)
            return false;
        SetStatus("시작 중...");
        return true;
    }

    public void SetStatus(string status) => Volatile.Write(ref _status, status);

    public (bool IsTraining, string Status) Snapshot() =>
        (Volatile.Read(ref _isTraining) != 0, Volatile.Read(ref _status));

    public void End() => Interlocked.Exchange(ref _isTraining, 0);
}
