namespace StockTrader.Models;

public sealed class FinancialAuthorityFence
{
    public required string TransitionId { get; set; }
    public long AuthorityGeneration { get; set; }
    public required string NewEntryAcceptance { get; set; }
    public required string ManualCommandAcceptance { get; set; }
    public required string PositionCycle { get; set; }
    public required string EntryReconciliation { get; set; }
    public required string PositionReconciliation { get; set; }
    public DateTime? LastCompletedPositionBarUtc { get; set; }
    public int UnresolvedIntentCount { get; set; }
    public int UnresolvedBrokerEffectCount { get; set; }
    public long ActivityJournalCount { get; set; }
    public long EnabledConsumerLag { get; set; }
    public required string FenceHash { get; set; }
    public bool IsReleased { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class FinancialAuthorityMirror
{
    public int Id { get; set; } = 1;
    public long AuthorityGeneration { get; set; }
    public required string Mode { get; set; }
    public required string Owner { get; set; }
    public required string TransitionId { get; set; }
    public required string ReceiptHash { get; set; }
    public DateTime MirroredAtUtc { get; set; }
}

public static class FinancialPositionCycleStates
{
    public const string Active = "Active";
    public const string Finishing = "Finishing";
    public const string AtBarrier = "AtBarrier";
    public const string Absent = "Absent";
}

public static class FinancialReconciliationStates
{
    public const string Active = "Active";
    public const string Draining = "Draining";
    public const string Clear = "Clear";
    public const string Absent = "Absent";
}
