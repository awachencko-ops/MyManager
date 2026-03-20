using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Replica.VerifyTests;

public sealed class FileOperationRetryPolicyTests
{
    [Fact]
    public void Execute_WhenTransientFailure_RetriesAndEventuallySucceeds()
    {
        var attempt = 0;
        var delays = new List<TimeSpan>();

        var policy = new FileOperationRetryPolicy(
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(10),
            backoffMultiplier: 2d,
            delay: delays.Add);

        var result = policy.Execute(
            operation: "copy-test",
            path: "x",
            action: () =>
            {
                attempt++;
                if (attempt < 3)
                    throw new IOException("locked");

                return 42;
            });

        Assert.Equal(42, result);
        Assert.Equal(3, attempt);
        Assert.Equal(2, delays.Count);
        Assert.Equal(10, (int)delays[0].TotalMilliseconds);
        Assert.Equal(20, (int)delays[1].TotalMilliseconds);
    }

    [Fact]
    public void Execute_WhenTransientFailurePersists_ThrowsAfterMaxAttempts()
    {
        var attempt = 0;
        var warnings = new List<string>();
        var errors = new List<string>();

        var policy = new FileOperationRetryPolicy(
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(5),
            warnLogger: warnings.Add,
            errorLogger: errors.Add,
            delay: _ => { });

        var ex = Assert.Throws<IOException>(() =>
            policy.Execute(
                operation: "move-test",
                path: "y",
                action: () =>
                {
                    attempt++;
                    throw new IOException("network");
                }));

        Assert.Contains("network", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, attempt);
        Assert.Equal(2, warnings.Count);
        Assert.Single(errors);
        Assert.Contains("exhausted", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WhenNonTransientFailure_DoesNotRetry()
    {
        var attempt = 0;
        var warnings = new List<string>();

        var policy = new FileOperationRetryPolicy(
            maxAttempts: 4,
            warnLogger: warnings.Add,
            delay: _ => { });

        Assert.Throws<InvalidOperationException>(() =>
            policy.Execute(
                operation: "logic-test",
                path: "z",
                action: () =>
                {
                    attempt++;
                    throw new InvalidOperationException("bad state");
                }));

        Assert.Equal(1, attempt);
        Assert.Empty(warnings);
    }
}
