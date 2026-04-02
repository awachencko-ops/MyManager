using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AvaloniaOrdersPrototype;

public sealed class OrderNode : INotifyPropertyChanged
{
    private readonly int _leafItemsCount;
    private bool _isExpanded;

    private OrderNode(
        string orderNumber,
        bool isGroup,
        string status,
        string client,
        int leafItemsCount,
        DateTime updatedAt)
    {
        OrderNumber = orderNumber;
        IsGroup = isGroup;
        Status = status;
        Client = client;
        _leafItemsCount = leafItemsCount;
        UpdatedAt = updatedAt;
        Children = new ObservableCollection<OrderNode>();
        Children.CollectionChanged += OnChildrenCollectionChanged;
    }

    public string OrderNumber { get; }

    public bool IsGroup { get; }

    public string Status { get; }

    public string Client { get; }

    public DateTime UpdatedAt { get; }

    public ObservableCollection<OrderNode> Children { get; }

    public string KindLabel => IsGroup ? "Group" : "Single";

    public int ItemsCount => IsGroup ? Children.Sum(static child => child.ItemsCount) : _leafItemsCount;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public static OrderNode CreateSingle(
        string orderNumber,
        string status,
        string client,
        int itemsCount,
        DateTime updatedAt)
    {
        return new OrderNode(orderNumber, isGroup: false, status, client, itemsCount, updatedAt);
    }

    public static OrderNode CreateGroup(
        string orderNumber,
        string status,
        string client,
        DateTime updatedAt)
    {
        return new OrderNode(orderNumber, isGroup: true, status, client, leafItemsCount: 0, updatedAt);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ItemsCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class DemoOrderFactory
{
    public static ObservableCollection<OrderNode> Create()
    {
        var result = new ObservableCollection<OrderNode>
        {
            OrderNode.CreateSingle(
                "00071",
                "[WORK] In progress",
                "Cafe menu",
                1,
                DateTime.Today.AddHours(9).AddMinutes(24)),
            OrderNode.CreateSingle(
                "00072",
                "[NEW] New",
                "Poster A2",
                2,
                DateTime.Today.AddHours(10).AddMinutes(12)),
            OrderNode.CreateSingle(
                "00073",
                "[PLAN] Planned",
                "Business cards",
                3,
                DateTime.Today.AddHours(11).AddMinutes(5))
        };

        var printGroup = OrderNode.CreateGroup(
            "GRP-PRINT-01",
            "[WORK] In progress",
            "Print group",
            DateTime.Today.AddHours(11).AddMinutes(47));

        printGroup.Children.Add(
            OrderNode.CreateSingle(
                "00074",
                "[WORK] In progress",
                "A3 labels",
                1,
                DateTime.Today.AddHours(11).AddMinutes(49)));

        printGroup.Children.Add(
            OrderNode.CreateSingle(
                "00075",
                "[HOLD] On hold",
                "Stickers",
                2,
                DateTime.Today.AddHours(11).AddMinutes(55)));

        var nestedGroup = OrderNode.CreateGroup(
            "GRP-INNER-01",
            "[PLAN] Planned",
            "Nested batch",
            DateTime.Today.AddHours(12).AddMinutes(8));

        nestedGroup.Children.Add(
            OrderNode.CreateSingle(
                "00076",
                "[DONE] Done",
                "Gift cards",
                1,
                DateTime.Today.AddHours(12).AddMinutes(10)));

        printGroup.Children.Add(nestedGroup);
        printGroup.IsExpanded = true;

        var packageGroup = OrderNode.CreateGroup(
            "GRP-PACK-03",
            "[NEW] New",
            "Packaging",
            DateTime.Today.AddHours(12).AddMinutes(34));

        packageGroup.Children.Add(
            OrderNode.CreateSingle(
                "00077",
                "[NEW] New",
                "Box sleeves",
                4,
                DateTime.Today.AddHours(12).AddMinutes(40)));

        result.Add(printGroup);
        result.Add(packageGroup);

        return result;
    }
}
