using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Interactivity;

namespace AvaloniaOrdersPrototype;

public partial class MainWindow : Window
{
    private static readonly string[] StatusPalette =
    {
        "[NEW] New",
        "[PLAN] Planned",
        "[WORK] In progress",
        "[DONE] Done",
        "[HOLD] On hold"
    };

    private readonly ObservableCollection<OrderNode> _rootOrders;
    private readonly Random _random = new();
    private HierarchicalTreeDataGridSource<OrderNode>? _source;
    private int _groupCounter = 10;
    private int _orderCounter = 90500;

    public MainWindow()
    {
        InitializeComponent();
        _rootOrders = DemoOrderFactory.Create();
        ConfigureGrid();
        UpdateSummary();
    }

    private void ConfigureGrid()
    {
        _source = new HierarchicalTreeDataGridSource<OrderNode>(_rootOrders);

        _source.Columns.Add(
            new HierarchicalExpanderColumn<OrderNode>(
                new TextColumn<OrderNode, string>(
                    "Order No",
                    order => order.OrderNumber,
                    width: new GridLength(2, GridUnitType.Star)),
                order => order.Children,
                order => order.Children.Count > 0,
                order => order.IsExpanded));

        _source.Columns.Add(
            new TextColumn<OrderNode, string>(
                "Status",
                order => order.Status,
                width: new GridLength(180)));

        _source.Columns.Add(
            new TextColumn<OrderNode, string>(
                "Kind",
                order => order.KindLabel,
                width: new GridLength(120)));

        _source.Columns.Add(
            new TextColumn<OrderNode, string>(
                "Client",
                order => order.Client,
                width: new GridLength(2, GridUnitType.Star)));

        _source.Columns.Add(
            new TextColumn<OrderNode, int>(
                "Items",
                order => order.ItemsCount,
                width: new GridLength(90)));

        _source.Columns.Add(
            new TextColumn<OrderNode, DateTime>(
                "Updated",
                order => order.UpdatedAt,
                width: new GridLength(170),
                options: new TextColumnOptions<OrderNode>
                {
                    StringFormat = "yyyy-MM-dd HH:mm"
                }));

        OrdersGrid.Source = _source;

        if (_source.RowSelection is not null)
        {
            _source.RowSelection.SelectionChanged += (_, _) => UpdateSummary();
        }
    }

    private void ExpandAllClick(object? sender, RoutedEventArgs e)
    {
        _source?.ExpandAll();
    }

    private void CollapseAllClick(object? sender, RoutedEventArgs e)
    {
        _source?.CollapseAll();
    }

    private void AddGroupClick(object? sender, RoutedEventArgs e)
    {
        var group = OrderNode.CreateGroup(
            $"G-{DateTime.Now:ddHHmm}-{_groupCounter++:00}",
            PickStatus(),
            "Generated group",
            DateTime.Now);

        group.Children.Add(
            OrderNode.CreateSingle(
                GenerateOrderNumber(),
                PickStatus(),
                "Generated child A",
                _random.Next(1, 7),
                DateTime.Now));

        group.Children.Add(
            OrderNode.CreateSingle(
                GenerateOrderNumber(),
                PickStatus(),
                "Generated child B",
                _random.Next(1, 7),
                DateTime.Now));

        group.IsExpanded = true;
        _rootOrders.Insert(_random.Next(0, _rootOrders.Count + 1), group);

        UpdateSummary();
    }

    private void AddSingleClick(object? sender, RoutedEventArgs e)
    {
        _rootOrders.Insert(
            _random.Next(0, _rootOrders.Count + 1),
            OrderNode.CreateSingle(
                GenerateOrderNumber(),
                PickStatus(),
                "Generated single order",
                _random.Next(1, 7),
                DateTime.Now));

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var flat = Flatten(_rootOrders).ToList();
        var groups = flat.Count(order => order.IsGroup);
        var singles = flat.Count - groups;

        var selected = _source?.RowSelection?.SelectedItem;
        var selectedText = selected is null ? "-" : selected.OrderNumber;

        SummaryText.Text = $"Rows: {flat.Count} | Groups: {groups} | Singles: {singles} | Selected: {selectedText}";
    }

    private string PickStatus()
    {
        return StatusPalette[_random.Next(StatusPalette.Length)];
    }

    private string GenerateOrderNumber()
    {
        _orderCounter++;
        return _orderCounter.ToString("00000");
    }

    private static IEnumerable<OrderNode> Flatten(IEnumerable<OrderNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;

            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }
}
