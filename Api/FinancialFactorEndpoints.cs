using StockTrader.Api.Contracts;
using StockTrader.Application.Research;
using StockTrader.BackgroundServices;
using StockTrader.Services.Financial;

namespace StockTrader.Api;

public static class FinancialFactorEndpoints
{
    public static RouteGroupBuilder MapFinancialFactorApi(this RouteGroupBuilder group)
    {
        group.MapGet("/financial-factors/meta", async (
            FinancialFactorQueryService service,
            CancellationToken ct) =>
                Results.Ok(FinancialFactorMetaResponse.Create(
                    await service.GetMetaAsync(ct))))
            .Produces<FinancialFactorMetaResponse>()
            .RequireAuthorization();

        group.MapGet("/financial-factors/query", async (
            decimal? peRatioMax,
            decimal? pbRatioMax,
            decimal? roePercentMin,
            decimal? operatingMarginMin,
            decimal? revenueGrowthMin,
            decimal? netIncomeGrowthMin,
            bool? turnaroundOnly,
            bool? positiveEarningsOnly,
            string? symbols,
            string? sectors,
            string? industries,
            string? search,
            int? limit,
            string? sortBy,
            FinancialFactorQueryService service,
            CancellationToken ct) =>
        {
            var result = await service.QueryAsync(new FinancialFactorQuery(
                peRatioMax,
                pbRatioMax,
                roePercentMin,
                operatingMarginMin,
                revenueGrowthMin,
                netIncomeGrowthMin,
                turnaroundOnly,
                positiveEarningsOnly,
                symbols,
                sectors,
                industries,
                search,
                limit,
                sortBy), ct);
            return Results.Ok(FinancialFactorQueryResponse.Create(result));
        })
            .Produces<FinancialFactorQueryResponse>()
            .RequireAuthorization();

        group.MapPost("/financial-factors/import", async (
            List<FinancialSnapshotImportDto>? items,
            FinancialSnapshotImportService importService,
            CancellationToken ct) =>
        {
            if (items is null || items.Count == 0)
            {
                return Results.BadRequest(new FinancialImportErrorResponse(
                    "Import items are required."));
            }

            if (!items.Any(item => !string.IsNullOrWhiteSpace(item.Symbol)))
            {
                return Results.BadRequest(new FinancialImportErrorResponse(
                    "At least one valid symbol is required."));
            }

            var summary = await importService.UpsertAsync(
                items.Select(item => item.ToItem()),
                ct);
            return Results.Ok(new FinancialImportResponse(
                summary.ImportedCount,
                summary.SkippedCount));
        })
            .Accepts<List<FinancialSnapshotImportDto>>("application/json")
            .Produces<FinancialImportResponse>()
            .Produces<FinancialImportErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapGet("/financial-factors/pipeline/status", async (
            FinancialFactorQueryService queryService,
            FinancialSnapshotIngestionService pipeline,
            SecFinancialSnapshotSyncService vendorSync,
            CancellationToken ct) =>
        {
            var history = await queryService.GetImportRunHistoryAsync(
                ResearchUniversePolicy.RecentImportRunLimit,
                ct);
            var vendorStatus = await vendorSync.GetStatusAsync(ct);
            return Results.Ok(new FinancialPipelineStatusResponse(
                pipeline.Enabled,
                pipeline.GetResolvedImportDirectory(),
                pipeline.ScanIntervalMinutes,
                history.LatestSuccessfulRun?.CompletedAt?.ToString("o"),
                FinancialVendorSyncStatusResponse.Create(vendorStatus),
                history.RecentRuns.Select(FinancialImportRunResponse.Create).ToArray()));
        })
            .Produces<FinancialPipelineStatusResponse>()
            .RequireAuthorization();

        group.MapPost("/financial-factors/pipeline/run", async (
            FinancialSnapshotIngestionService pipeline,
            CancellationToken ct) =>
                Results.Ok(FinancialPipelineRunResponse.Create(
                    await pipeline.RunScanAsync(ct))))
            .Produces<FinancialPipelineRunResponse>()
            .RequireAuthorization();

        group.MapPost("/financial-factors/vendor-sync/run", async (
            FinancialVendorSyncRequest? request,
            SecFinancialSnapshotSyncService vendorSync,
            CancellationToken ct) =>
        {
            var symbols = ResearchFilterPolicy.ParseCsv(request?.Symbols).ToArray();
            return Results.Ok(FinancialPipelineRunResponse.Create(
                await vendorSync.RunSyncAsync(symbols, ct, force: true)));
        })
            .Accepts<FinancialVendorSyncRequest>("application/json")
            .Produces<FinancialPipelineRunResponse>()
            .RequireAuthorization();

        return group;
    }
}
