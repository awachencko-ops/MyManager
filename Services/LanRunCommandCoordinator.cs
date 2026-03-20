using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared.Models;

namespace Replica;

public sealed class LanRunCommandCoordinator
{
    private readonly ILanOrderRunApiGateway _lanApiGateway;

    public LanRunCommandCoordinator(ILanOrderRunApiGateway lanApiGateway)
    {
        _lanApiGateway = lanApiGateway ?? throw new ArgumentNullException(nameof(lanApiGateway));
    }

    public async Task<LanRunBatchResult> TryStartRunsAsync(
        IReadOnlyCollection<OrderData> candidateOrders,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        Func<OrderData, string> orderDisplayIdResolver,
        CancellationToken cancellationToken = default)
    {
        var approvedOrders = new List<OrderData>();
        var skippedByServer = new List<string>();

        if (candidateOrders == null || candidateOrders.Count == 0)
            return LanRunBatchResult.Success(approvedOrders, skippedByServer, usedLanApi: useLanApi);

        foreach (var order in candidateOrders.Where(order => order != null))
        {
            if (!useLanApi)
            {
                approvedOrders.Add(order);
                continue;
            }

            var apiResult = await _lanApiGateway.StartRunAsync(
                lanApiBaseUrl,
                order.InternalId,
                order.StorageVersion,
                actor,
                cancellationToken);

            if (apiResult.IsSuccess)
            {
                ApplyLanApiOrderSnapshot(order, apiResult.Order);
                approvedOrders.Add(order);
                continue;
            }

            if (apiResult.CurrentVersion > 0)
                order.StorageVersion = apiResult.CurrentVersion;

            if (apiResult.IsConflict || apiResult.IsBadRequest || apiResult.IsNotFound)
            {
                var orderDisplayId = orderDisplayIdResolver?.Invoke(order) ?? order.InternalId;
                var reason = string.IsNullOrWhiteSpace(apiResult.Error)
                    ? "server rejected run command"
                    : apiResult.Error;
                skippedByServer.Add($"{orderDisplayId}: {reason}");
                continue;
            }

            var fatalReason = string.IsNullOrWhiteSpace(apiResult.Error)
                ? "LAN API is unavailable"
                : apiResult.Error;
            return LanRunBatchResult.Fatal(fatalReason);
        }

        return LanRunBatchResult.Success(approvedOrders, skippedByServer, usedLanApi: useLanApi);
    }

    public async Task<LanRunStopCommandResult> TryStopRunAsync(
        OrderData order,
        bool useLanApi,
        string lanApiBaseUrl,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (order == null || !useLanApi)
            return LanRunStopCommandResult.NotUsed();

        var apiResult = await _lanApiGateway.StopRunAsync(
            lanApiBaseUrl,
            order.InternalId,
            order.StorageVersion,
            actor,
            cancellationToken);

        if (apiResult.IsSuccess)
            ApplyLanApiOrderSnapshot(order, apiResult.Order);
        else if (apiResult.CurrentVersion > 0)
            order.StorageVersion = apiResult.CurrentVersion;

        return LanRunStopCommandResult.FromApi(apiResult);
    }

    private static void ApplyLanApiOrderSnapshot(OrderData localOrder, SharedOrder? apiOrder)
    {
        if (localOrder == null || apiOrder == null)
            return;

        if (apiOrder.Version > 0)
            localOrder.StorageVersion = apiOrder.Version;
        if (!string.IsNullOrWhiteSpace(apiOrder.Status))
            localOrder.Status = apiOrder.Status.Trim();
        if (!string.IsNullOrWhiteSpace(apiOrder.LastStatusSource))
            localOrder.LastStatusSource = apiOrder.LastStatusSource.Trim();
        if (!string.IsNullOrWhiteSpace(apiOrder.LastStatusReason))
            localOrder.LastStatusReason = apiOrder.LastStatusReason.Trim();
        if (apiOrder.LastStatusAt != default)
            localOrder.LastStatusAt = apiOrder.LastStatusAt;
    }
}

public sealed class LanRunBatchResult
{
    private LanRunBatchResult(
        bool isFatal,
        string fatalError,
        List<OrderData> approvedOrders,
        List<string> skippedByServer,
        bool usedLanApi)
    {
        IsFatal = isFatal;
        FatalError = fatalError ?? string.Empty;
        ApprovedOrders = approvedOrders ?? new List<OrderData>();
        SkippedByServer = skippedByServer ?? new List<string>();
        UsedLanApi = usedLanApi;
    }

    public bool IsFatal { get; }
    public string FatalError { get; }
    public List<OrderData> ApprovedOrders { get; }
    public List<string> SkippedByServer { get; }
    public bool UsedLanApi { get; }

    public static LanRunBatchResult Success(List<OrderData> approvedOrders, List<string> skippedByServer, bool usedLanApi)
    {
        return new LanRunBatchResult(
            isFatal: false,
            fatalError: string.Empty,
            approvedOrders,
            skippedByServer,
            usedLanApi);
    }

    public static LanRunBatchResult Fatal(string fatalError)
    {
        return new LanRunBatchResult(
            isFatal: true,
            fatalError: fatalError,
            approvedOrders: new List<OrderData>(),
            skippedByServer: new List<string>(),
            usedLanApi: true);
    }
}

public sealed class LanRunStopCommandResult
{
    private LanRunStopCommandResult(bool usedLanApi, LanOrderRunApiResult? apiResult)
    {
        UsedLanApi = usedLanApi;
        ApiResult = apiResult;
    }

    public bool UsedLanApi { get; }
    public LanOrderRunApiResult? ApiResult { get; }

    public static LanRunStopCommandResult NotUsed()
    {
        return new LanRunStopCommandResult(usedLanApi: false, apiResult: null);
    }

    public static LanRunStopCommandResult FromApi(LanOrderRunApiResult apiResult)
    {
        return new LanRunStopCommandResult(usedLanApi: true, apiResult: apiResult);
    }
}
