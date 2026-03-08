namespace StockTrader.BackgroundServices;

/// <summary>
/// Provides retry logic with exponential backoff for background service operations.
/// </summary>
public static class RetryHelper
{
    /// <summary>
    /// Executes an async action with retry logic and exponential backoff.
    /// Delays: attempt 1 = 1s, attempt 2 = 2s, attempt 3 = 4s (2^(attempt-1)).
    /// </summary>
    /// <param name="action">The async operation to execute.</param>
    /// <param name="logger">Logger for retry warnings and final error.</param>
    /// <param name="operationName">Human-readable name for structured logging.</param>
    /// <param name="maxRetries">Maximum number of attempts (default: 3).</param>
    /// <param name="ct">Cancellation token — cancellation is NOT retried.</param>
    /// <exception cref="OperationCanceledException">Re-thrown immediately without retry.</exception>
    /// <exception cref="Exception">Re-thrown after all retries are exhausted.</exception>
    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        ILogger logger,
        string operationName,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Propagate cancellation immediately — no retry on shutdown.
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    logger.LogError(ex,
                        "{Operation} failed after {MaxRetries} attempts — giving up",
                        operationName, maxRetries);
                    throw;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
                logger.LogWarning(ex,
                    "{Operation} failed (attempt {Attempt}/{MaxRetries}), retrying in {DelaySeconds}s",
                    operationName, attempt, maxRetries, delay.TotalSeconds);

                await Task.Delay(delay, ct);
            }
        }
    }
}
