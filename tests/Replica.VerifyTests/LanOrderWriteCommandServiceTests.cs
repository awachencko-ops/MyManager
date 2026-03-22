using System;
using System.Threading;
using System.Threading.Tasks;
using Replica.Shared.Models;
using Xunit;

namespace Replica.VerifyTests;

public sealed class LanOrderWriteCommandServiceTests
{
    [Fact]
    public async Task TryCreateOrderAsync_MapsApiPayloadToLocalOrder()
    {
        var gateway = new StubGateway
        {
            CreateResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-1",
                OrderNumber = "1001",
                UserName = "operator-1",
                Status = "Waiting",
                Version = 7,
                ManagerOrderDate = new DateTime(2026, 3, 23),
                ArrivalDate = new DateTime(2026, 3, 23, 10, 0, 0),
                Items =
                {
                    new SharedOrderItem
                    {
                        ItemId = "i-1",
                        SequenceNo = 0,
                        FileStatus = "Waiting",
                        SourcePath = @"C:\orders\1001\in.pdf"
                    }
                }
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var result = await service.TryCreateOrderAsync(
            new OrderData
            {
                Id = "1001",
                UserName = " operator-1 "
            },
            "http://localhost:5000/",
            "operator-1",
            user => user.Trim());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal("o-1", result.Order!.InternalId);
        Assert.Equal(7, result.Order.StorageVersion);
        Assert.Equal("1001", result.Order.Id);
        Assert.Single(result.Order.Items);
        Assert.Equal(@"C:\orders\1001\in.pdf", result.Order.SourcePath);
        Assert.NotNull(gateway.LastCreateRequest);
        Assert.Equal("1001", gateway.LastCreateRequest!.OrderNumber);
        Assert.Equal("operator-1", gateway.LastCreateRequest.UserName);
    }

    [Fact]
    public async Task TryUpdateOrderAsync_UsesExpectedVersionAndOrderFields()
    {
        var gateway = new StubGateway
        {
            UpdateResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-2",
                OrderNumber = "2002",
                UserName = "operator-2",
                Status = "Waiting",
                Version = 15,
                ManagerOrderDate = new DateTime(2026, 3, 24),
                ArrivalDate = new DateTime(2026, 3, 23, 11, 0, 0)
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var currentOrder = new OrderData
        {
            InternalId = "o-2",
            StorageVersion = 14,
            Id = "2001"
        };

        var updatedOrder = new OrderData
        {
            Id = "2002",
            OrderDate = new DateTime(2026, 3, 24),
            UserName = " operator-2 ",
            Status = "Waiting",
            Keyword = "kw",
            FolderName = "folder-a",
            PitStopAction = "ps",
            ImposingAction = "im"
        };

        var result = await service.TryUpdateOrderAsync(
            currentOrder,
            updatedOrder,
            "http://localhost:5000/",
            "operator-2",
            user => user.Trim());

        Assert.True(result.IsSuccess);
        Assert.NotNull(gateway.LastUpdateRequest);
        Assert.Equal(14, gateway.LastUpdateRequest!.ExpectedVersion);
        Assert.Equal("2002", gateway.LastUpdateRequest.OrderNumber);
        Assert.Equal(new DateTime(2026, 3, 24), gateway.LastUpdateRequest.ManagerOrderDate);
        Assert.Equal("operator-2", gateway.LastUpdateRequest.UserName);
        Assert.Equal("Waiting", gateway.LastUpdateRequest.Status);
        Assert.NotNull(result.Order);
        Assert.Equal(15, result.Order!.StorageVersion);
    }

    private sealed class StubGateway : ILanOrderWriteApiGateway
    {
        public LanCreateOrderRequest? LastCreateRequest { get; private set; }
        public LanUpdateOrderRequest? LastUpdateRequest { get; private set; }
        public LanOrderWriteApiResult CreateResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult UpdateResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");

        public Task<LanOrderWriteApiResult> CreateOrderAsync(
            string apiBaseUrl,
            LanCreateOrderRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastCreateRequest = request;
            return Task.FromResult(CreateResponse);
        }

        public Task<LanOrderWriteApiResult> UpdateOrderAsync(
            string apiBaseUrl,
            string orderInternalId,
            LanUpdateOrderRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastUpdateRequest = request;
            return Task.FromResult(UpdateResponse);
        }
    }
}
