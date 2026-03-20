using System;
using System.Collections.Generic;

namespace Replica;

public sealed class DependencyBulkheadPolicy
{
    private readonly int _defaultLimit;
    private readonly Dictionary<string, int> _limits;
    private readonly Dictionary<string, int> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public DependencyBulkheadPolicy(int defaultLimit, IReadOnlyDictionary<string, int>? limits = null)
    {
        _defaultLimit = Math.Max(1, defaultLimit);
        _limits = limits == null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(limits, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryEnter(string dependencyName, out IDisposable? lease, out int inFlightAfterEnter, out int limit)
    {
        lease = null;
        inFlightAfterEnter = 0;
        limit = ResolveLimit(dependencyName);

        lock (_sync)
        {
            _inFlight.TryGetValue(dependencyName, out var current);
            if (current >= limit)
            {
                inFlightAfterEnter = current;
                return false;
            }

            var next = current + 1;
            _inFlight[dependencyName] = next;
            inFlightAfterEnter = next;
            lease = new Lease(this, dependencyName);
            return true;
        }
    }

    private int ResolveLimit(string dependencyName)
    {
        if (!string.IsNullOrWhiteSpace(dependencyName)
            && _limits.TryGetValue(dependencyName, out var configured))
        {
            return Math.Max(1, configured);
        }

        return _defaultLimit;
    }

    private void Exit(string dependencyName)
    {
        lock (_sync)
        {
            if (!_inFlight.TryGetValue(dependencyName, out var current) || current <= 0)
                return;

            if (current == 1)
            {
                _inFlight.Remove(dependencyName);
                return;
            }

            _inFlight[dependencyName] = current - 1;
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly DependencyBulkheadPolicy _owner;
        private readonly string _dependencyName;
        private bool _disposed;

        public Lease(DependencyBulkheadPolicy owner, string dependencyName)
        {
            _owner = owner;
            _dependencyName = dependencyName;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner.Exit(_dependencyName);
        }
    }
}
