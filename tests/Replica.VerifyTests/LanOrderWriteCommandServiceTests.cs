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

    [Fact]
    public async Task TryReorderItemsAsync_SendsSortedOrderedIdsAndVersion()
    {
        var gateway = new StubGateway
        {
            ReorderResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-3",
                Version = 22,
                Items =
                {
                    new SharedOrderItem { ItemId = "i-2", SequenceNo = 0, FileStatus = "Waiting" },
                    new SharedOrderItem { ItemId = "i-1", SequenceNo = 1, FileStatus = "Waiting" }
                }
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var order = new OrderData
        {
            InternalId = "o-3",
            StorageVersion = 21,
            Items =
            {
                new OrderFileItem { ItemId = "i-1", SequenceNo = 5 },
                new OrderFileItem { ItemId = "i-2", SequenceNo = 1 }
            }
        };

        var result = await service.TryReorderItemsAsync(
            order,
            "http://localhost:5000/",
            "operator-3");

        Assert.True(result.IsSuccess);
        Assert.NotNull(gateway.LastReorderRequest);
        Assert.Equal(21, gateway.LastReorderRequest!.ExpectedOrderVersion);
        Assert.Equal(new[] { "i-2", "i-1" }, gateway.LastReorderRequest.OrderedItemIds);
        Assert.NotNull(result.Order);
        Assert.Equal(22, result.Order!.StorageVersion);
    }

    [Fact]
    public async Task TryUpsertItemAsync_WhenItemVersionIsZero_UsesAddEndpoint()
    {
        var gateway = new StubGateway
        {
            AddItemResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-4",
                Version = 8,
                Items =
                {
                    new SharedOrderItem
                    {
                        ItemId = "i-new",
                        SequenceNo = 0,
                        Version = 1,
                        FileStatus = "Waiting"
                    }
                }
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var order = new OrderData
        {
            InternalId = "o-4",
            StorageVersion = 7
        };
        var item = new OrderFileItem
        {
            ItemId = "i-new",
            SequenceNo = 0,
            StorageVersion = 0,
            FileStatus = "Ожидание"
        };

        var result = await service.TryUpsertItemAsync(
            order,
            item,
            "http://localhost:5000/",
            "operator-4");

        Assert.True(result.IsSuccess);
        Assert.NotNull(gateway.LastAddItemRequest);
        Assert.Null(gateway.LastUpdateItemRequest);
        Assert.Equal(7, gateway.LastAddItemRequest!.ExpectedOrderVersion);
        Assert.Equal("i-new", gateway.LastAddItemRequest.Item.ItemId);
        Assert.NotNull(result.Order);
        Assert.Equal(8, result.Order!.StorageVersion);
    }

    [Fact]
    public async Task TryUpsertItemAsync_WhenItemVersionIsPositive_UsesUpdateEndpoint()
    {
        var gateway = new StubGateway
        {
            UpdateItemResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-5",
                Version = 30,
                Items =
                {
                    new SharedOrderItem
                    {
                        ItemId = "i-5",
                        SequenceNo = 0,
                        Version = 10,
                        FileStatus = "Waiting"
                    }
                }
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var order = new OrderData
        {
            InternalId = "o-5",
            StorageVersion = 29
        };
        var item = new OrderFileItem
        {
            ItemId = "i-5",
            StorageVersion = 9,
            FileStatus = "Ожидание"
        };

        var result = await service.TryUpsertItemAsync(
            order,
            item,
            "http://localhost:5000/",
            "operator-5");

        Assert.True(result.IsSuccess);
        Assert.Null(gateway.LastAddItemRequest);
        Assert.NotNull(gateway.LastUpdateItemRequest);
        Assert.Equal(29, gateway.LastUpdateItemRequest!.ExpectedOrderVersion);
        Assert.Equal(9, gateway.LastUpdateItemRequest.ExpectedItemVersion);
        Assert.Equal("i-5", gateway.LastUpdateItemId);
        Assert.NotNull(result.Order);
        Assert.Equal(30, result.Order!.StorageVersion);
    }

    [Fact]
    public async Task TryDeleteItemAsync_UsesDeleteEndpointWithExpectedVersions()
    {
        var gateway = new StubGateway
        {
            DeleteItemResponse = LanOrderWriteApiResult.Success(new SharedOrder
            {
                InternalId = "o-6",
                Version = 41,
                Items =
                {
                    new SharedOrderItem
                    {
                        ItemId = "i-6b",
                        SequenceNo = 0,
                        Version = 12,
                        FileStatus = "Waiting"
                    }
                }
            })
        };

        var service = new LanOrderWriteCommandService(gateway);
        var order = new OrderData
        {
            InternalId = "o-6",
            StorageVersion = 40
        };
        var item = new OrderFileItem
        {
            ItemId = "i-6a",
            StorageVersion = 5
        };

        var result = await service.TryDeleteItemAsync(
            order,
            item,
            "http://localhost:5000/",
            "operator-6");

        Assert.True(result.IsSuccess);
        Assert.NotNull(gateway.LastDeleteItemRequest);
        Assert.Equal(40, gateway.LastDeleteItemRequest!.ExpectedOrderVersion);
        Assert.Equal(5, gateway.LastDeleteItemRequest.ExpectedItemVersion);
        Assert.Equal("i-6a", gateway.LastDeleteItemId);
        Assert.NotNull(result.Order);
        Assert.Equal(41, result.Order!.StorageVersion);
    }

    private sealed class StubGateway : ILanOrderWriteApiGateway
    {
        public LanCreateOrderRequest? LastCreateRequest { get; private set; }
        public LanUpdateOrderRequest? LastUpdateRequest { get; private set; }
        public LanReorderOrderItemsRequest? LastReorderRequest { get; private set; }
        public LanAddOrderItemRequest? LastAddItemRequest { get; private set; }
        public LanUpdateOrderItemRequest? LastUpdateItemRequest { get; private set; }
        public string? LastUpdateItemId { get; private set; }
        public LanDeleteOrderItemRequest? LastDeleteItemRequest { get; private set; }
        public string? LastDeleteItemId { get; private set; }
        public LanOrderWriteApiResult CreateResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult UpdateResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult ReorderResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult AddItemResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult UpdateItemResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");
        public LanOrderWriteApiResult DeleteItemResponse { get; set; } = LanOrderWriteApiResult.Failed("no response");

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

        public Task<LanOrderWriteApiResult> ReorderOrderItemsAsync(
            string apiBaseUrl,
            string orderInternalId,
            LanReorderOrderItemsRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastReorderRequest = request;
            return Task.FromResult(ReorderResponse);
        }

        public Task<LanOrderWriteApiResult> AddOrderItemAsync(
            string apiBaseUrl,
            string orderInternalId,
            LanAddOrderItemRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastAddItemRequest = request;
            return Task.FromResult(AddItemResponse);
        }

        public Task<LanOrderWriteApiResult> UpdateOrderItemAsync(
            string apiBaseUrl,
            string orderInternalId,
            string itemId,
            LanUpdateOrderItemRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastUpdateItemRequest = request;
            LastUpdateItemId = itemId;
            return Task.FromResult(UpdateItemResponse);
        }

        public Task<LanOrderWriteApiResult> DeleteOrderItemAsync(
            string apiBaseUrl,
            string orderInternalId,
            string itemId,
            LanDeleteOrderItemRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            LastDeleteItemRequest = request;
            LastDeleteItemId = itemId;
            return Task.FromResult(DeleteItemResponse);
        }
    }
}
