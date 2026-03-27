using Replica.Api.Application.Abstractions;
using Replica.Api.Application.Orders.Commands;
using Replica.Api.Application.Users.Commands;

namespace Replica.Api.Application.Validation;

public sealed class CreateOrderCommandValidator : IReplicaApiCommandValidator<CreateOrderCommand>
{
    public bool TryValidate(CreateOrderCommand command, out string error)
    {
        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.Request.OrderNumber))
        {
            error = "order number is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class DeleteOrderCommandValidator : IReplicaApiCommandValidator<DeleteOrderCommand>
{
    public bool TryValidate(DeleteOrderCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class UpdateOrderCommandValidator : IReplicaApiCommandValidator<UpdateOrderCommand>
{
    public bool TryValidate(UpdateOrderCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class AddOrderItemCommandValidator : IReplicaApiCommandValidator<AddOrderItemCommand>
{
    public bool TryValidate(AddOrderItemCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request?.Item == null)
        {
            error = "item payload is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class UpdateOrderItemCommandValidator : IReplicaApiCommandValidator<UpdateOrderItemCommand>
{
    public bool TryValidate(UpdateOrderItemCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId) || string.IsNullOrWhiteSpace(command.ItemId))
        {
            error = "order id and item id are required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class DeleteOrderItemCommandValidator : IReplicaApiCommandValidator<DeleteOrderItemCommand>
{
    public bool TryValidate(DeleteOrderItemCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId) || string.IsNullOrWhiteSpace(command.ItemId))
        {
            error = "order id and item id are required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class ReorderOrderItemsCommandValidator : IReplicaApiCommandValidator<ReorderOrderItemsCommand>
{
    public bool TryValidate(ReorderOrderItemsCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request?.OrderedItemIds == null)
        {
            error = "ordered item ids are required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class StartOrderRunCommandValidator : IReplicaApiCommandValidator<StartOrderRunCommand>
{
    public bool TryValidate(StartOrderRunCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class StopOrderRunCommandValidator : IReplicaApiCommandValidator<StopOrderRunCommand>
{
    public bool TryValidate(StopOrderRunCommand command, out string error)
    {
        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            error = "order id is required";
            return false;
        }

        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class UpsertUserCommandValidator : IReplicaApiCommandValidator<UpsertUserCommand>
{
    public bool TryValidate(UpsertUserCommand command, out string error)
    {
        if (command.Request == null)
        {
            error = "request body is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.Request.Name))
        {
            error = "user name is required";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

