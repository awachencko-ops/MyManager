using System;
using System.IO;
using System.Threading;

namespace Replica;

public sealed class FileOperationRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialDelay;
    private readonly double _backoffMultiplier;
    private readonly Action<string> _warnLogger;
    private readonly Action<string> _errorLogger;
    private readonly Action<TimeSpan> _delay;

    public FileOperationRetryPolicy(
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = 2d,
        Action<string>? warnLogger = null,
        Action<string>? errorLogger = null,
        Action<TimeSpan>? delay = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(200);
        _backoffMultiplier = backoffMultiplier < 1d ? 1d : backoffMultiplier;
        _warnLogger = warnLogger ?? (_ => { });
        _errorLogger = errorLogger ?? (_ => { });
        _delay = delay ?? Thread.Sleep;
    }

    public void Execute(string operation, string path, Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        Execute(operation, path, () =>
        {
            action();
            return true;
        });
    }

    public T Execute<T>(string operation, string path, Func<T> action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _maxAttempts)
            {
                var delay = CalculateDelay(attempt);
                _warnLogger(
                    $"FILE-RETRY | op={operation} | path={path} | attempt={attempt}/{_maxAttempts} | delay_ms={(int)delay.TotalMilliseconds} | {ex.Message}");
                _delay(delay);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _errorLogger(
                    $"FILE-RETRY | exhausted | op={operation} | path={path} | attempts={_maxAttempts} | {ex.Message}");
                throw;
            }
        }

        throw new InvalidOperationException("Unreachable retry state.");
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var factor = Math.Pow(_backoffMultiplier, Math.Max(0, attempt - 1));
        var nextMs = _initialDelay.TotalMilliseconds * factor;
        var boundedMs = Math.Min(nextMs, 2000d);
        return TimeSpan.FromMilliseconds(boundedMs);
    }

    internal static bool IsTransient(Exception ex)
    {
        return ex is IOException
            || ex is UnauthorizedAccessException;
    }
}
