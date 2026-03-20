using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Replica;

public sealed class OrderRunExecutionService
{
    public async Task<OrderRunExecutionResult> ExecuteAsync(
        IReadOnlyCollection<OrderRunStateService.RunSession> runSessions,
        Func<OrderData, CancellationToken, Task> runOrderAsync,
        Action<OrderData> onCancelled,
        Action<OrderData, Exception> onFailed,
        Action<OrderData> onCompleted)
    {
        if (runSessions == null || runSessions.Count == 0)
            return OrderRunExecutionResult.Empty;

        if (runOrderAsync == null)
            throw new ArgumentNullException(nameof(runOrderAsync));
        if (onCancelled == null)
            throw new ArgumentNullException(nameof(onCancelled));
        if (onFailed == null)
            throw new ArgumentNullException(nameof(onFailed));
        if (onCompleted == null)
            throw new ArgumentNullException(nameof(onCompleted));

        var errors = new ConcurrentQueue<OrderRunExecutionError>();
        var runTasks = runSessions
            .Where(session => session?.Order != null && session.Cts != null)
            .Select(session => ExecuteSingleAsync(
                session,
                runOrderAsync,
                onCancelled,
                onFailed,
                onCompleted,
                errors))
            .ToList();

        await Task.WhenAll(runTasks).ConfigureAwait(false);
        return new OrderRunExecutionResult(errors.ToList());
    }

    private static async Task ExecuteSingleAsync(
        OrderRunStateService.RunSession session,
        Func<OrderData, CancellationToken, Task> runOrderAsync,
        Action<OrderData> onCancelled,
        Action<OrderData, Exception> onFailed,
        Action<OrderData> onCompleted,
        ConcurrentQueue<OrderRunExecutionError> errors)
    {
        try
        {
            await runOrderAsync(session.Order, session.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            onCancelled(session.Order);
        }
        catch (Exception ex)
        {
            onFailed(session.Order, ex);
            errors.Enqueue(new OrderRunExecutionError(session.Order, ex.Message));
        }
        finally
        {
            onCompleted(session.Order);
        }
    }
}

public sealed record OrderRunExecutionResult(List<OrderRunExecutionError> Errors)
{
    public static readonly OrderRunExecutionResult Empty = new(new List<OrderRunExecutionError>());
}

public sealed record OrderRunExecutionError(OrderData Order, string Message);
