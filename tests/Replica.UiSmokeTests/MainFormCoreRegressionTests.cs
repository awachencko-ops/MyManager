using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Replica;

namespace Replica.UiSmokeTests;

public sealed class MainFormCoreRegressionTests
{
    [Fact]
    public void SR06_StatusTransitions_AreApplied()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var order = CreateOrder("SR06-001", WorkflowStatusNames.Waiting, "QA User", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1));
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", order);

            var processingChanged = (bool)(MainFormTestHarness.InvokePrivate(
                form,
                "SetOrderStatus",
                order,
                WorkflowStatusNames.Processing,
                "test",
                "sr06-processing",
                false,
                false) ?? false);
            Assert.True(processingChanged);
            Assert.Equal(WorkflowStatusNames.Processing, order.Status);

            MainFormTestHarness.InvokePrivate(
                form,
                "SetOrderStatus",
                order,
                WorkflowStatusNames.Cancelled,
                "test",
                "sr06-cancelled",
                false,
                false);
            Assert.Equal(WorkflowStatusNames.Cancelled, order.Status);

            MainFormTestHarness.InvokePrivate(
                form,
                "SetOrderStatus",
                order,
                WorkflowStatusNames.Error,
                "test",
                "sr06-error",
                false,
                false);
            Assert.Equal(WorkflowStatusNames.Error, order.Status);

            MainFormTestHarness.InvokePrivate(
                form,
                "SetOrderStatus",
                order,
                WorkflowStatusNames.Completed,
                "test",
                "sr06-completed",
                false,
                false);
            Assert.Equal(WorkflowStatusNames.Completed, order.Status);
        });
    }

    [Fact]
    public void SR07_StatusCellVisuals_AreRegistered_AndRowHeightIsStable()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var visuals = MainFormTestHarness.GetPrivateField<IDictionary>(form, "_statusCellVisuals");
            Assert.True(visuals.Contains(WorkflowStatusNames.Completed));
            Assert.True(visuals.Contains(WorkflowStatusNames.Error));
            Assert.True(visuals.Contains(WorkflowStatusNames.Cancelled));

            var completedVisual = visuals[WorkflowStatusNames.Completed];
            Assert.NotNull(completedVisual);

            var iconProperty = completedVisual!.GetType().GetProperty("Icon", BindingFlags.Instance | BindingFlags.Public);
            var iconBackgroundProperty = completedVisual.GetType().GetProperty("IconBackgroundColor", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(iconProperty);
            Assert.NotNull(iconBackgroundProperty);

            var completedIcon = iconProperty!.GetValue(completedVisual);
            var completedIconBackground = (Color)(iconBackgroundProperty!.GetValue(completedVisual) ?? Color.Empty);
            Assert.NotNull(completedIcon);
            Assert.Equal(Color.FromArgb(198, 234, 198), completedIconBackground);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            Assert.Equal(42, dgv.RowTemplate.Height);
            Assert.Equal(42, dgv.ColumnHeadersHeight);
        });
    }

    [Fact]
    public void SR08_SR09_SR10_Filters_WorkForStatusUserOrderAndDates()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var order1 = CreateOrder("1001", WorkflowStatusNames.Waiting, "QA User", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1));
            var order2 = CreateOrder("2002", WorkflowStatusNames.Error, "Operator", new DateTime(2026, 1, 2), new DateTime(2026, 1, 2));
            var order3 = CreateOrder("3003", WorkflowStatusNames.Completed, "QA User", new DateTime(2026, 1, 3), new DateTime(2026, 1, 3));

            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", order1);
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", order2);
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", order3);

            ResetAllFilters(form);

            var selectedStatuses = MainFormTestHarness.GetPrivateField<HashSet<string>>(form, "_selectedFilterStatuses");
            selectedStatuses.Clear();
            selectedStatuses.Add(WorkflowStatusNames.Error);
            MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
            Assert.Equal(new[] { "2002" }, GetVisibleOrderIds(form));

            ResetAllFilters(form);
            var selectedUsers = MainFormTestHarness.GetPrivateField<HashSet<string>>(form, "_selectedFilterUsers");
            selectedUsers.Clear();
            selectedUsers.Add("QA User");
            MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
            Assert.Equal(new[] { "1001", "3003" }, GetVisibleOrderIds(form));

            ResetAllFilters(form);
            MainFormTestHarness.SetPrivateField(form, "_orderNumberFilterText", "3003");
            MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
            Assert.Equal(new[] { "3003" }, GetVisibleOrderIds(form));

            ResetAllFilters(form);
            MainFormTestHarness.SetPrivateEnumFieldByValue(form, "_createdDateFilterKind", 2); // Single
            MainFormTestHarness.SetPrivateEnumFieldByValue(form, "_createdDateSingleMode", 0); // ExactDate
            MainFormTestHarness.SetPrivateField(form, "_createdDateSingleValue", new DateTime(2026, 1, 2));
            MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
            Assert.Equal(new[] { "2002" }, GetVisibleOrderIds(form));

            ResetAllFilters(form);
            MainFormTestHarness.SetPrivateEnumFieldByValue(form, "_receivedDateFilterKind", 3); // Range
            MainFormTestHarness.SetPrivateField(form, "_receivedDateRangeFrom", new DateTime(2026, 1, 1));
            MainFormTestHarness.SetPrivateField(form, "_receivedDateRangeTo", new DateTime(2026, 1, 2));
            MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
            Assert.Equal(new[] { "1001", "2002" }, GetVisibleOrderIds(form));
        });
    }

    [Fact]
    public void SR11_TrayIndicators_UpdateStatsConnectionDiskErrorsAndProgress()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var waitingOrder = CreateOrder("SR11-001", WorkflowStatusNames.Waiting, "QA User", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1));
            var errorOrder = CreateOrder("SR11-002", WorkflowStatusNames.Error, "Operator", new DateTime(2026, 1, 2), new DateTime(2026, 1, 2));
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", waitingOrder);
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", errorOrder);

            MainFormTestHarness.InvokePrivate(form, "RefreshTrayIndicators");

            var toolStats = MainFormTestHarness.GetPrivateField<ToolStripStatusLabel>(form, "toolStats");
            var toolConnection = MainFormTestHarness.GetPrivateField<ToolStripStatusLabel>(form, "toolConnection");
            var toolDiskFree = MainFormTestHarness.GetPrivateField<ToolStripStatusLabel>(form, "toolDiskFree");
            var toolAlerts = MainFormTestHarness.GetPrivateField<ToolStripStatusLabel>(form, "toolAlerts");
            var toolProgress = MainFormTestHarness.GetPrivateField<ToolStripProgressBar>(form, "toolProgress");

            Assert.StartsWith("\u0421\u0442\u0440\u043E\u043A:", toolStats.Text);
            Assert.Contains("\u0421\u0435\u0440\u0432\u0435\u0440:", toolConnection.Text);
            Assert.StartsWith("\u0421\u0432\u043E\u0431\u043E\u0434\u043D\u043E", toolDiskFree.Text);
            Assert.Contains("\u26A0", toolAlerts.Text);
            Assert.Contains("1", toolAlerts.Text);

            var progressByOrder = MainFormTestHarness.GetPrivateField<Dictionary<string, int>>(form, "_runProgressByOrderInternalId");
            progressByOrder[waitingOrder.InternalId] = 55;
            MainFormTestHarness.InvokePrivate(form, "UpdateTrayProgressIndicator");
            Assert.Equal(55, toolProgress.Value);
            Assert.Contains("55", toolProgress.ToolTipText);

            progressByOrder.Clear();
            MainFormTestHarness.InvokePrivate(form, "UpdateTrayProgressIndicator");
            Assert.Contains("\u041D\u0435\u0442", toolProgress.ToolTipText);
        });
    }

    [Fact]
    public void SR09_UsersDirectory_LoadsFromSource_AndWritesCache()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_UsersDirectory_SR09", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var sourcePath = Path.Combine(tempRootPath, "users.json");
            var cachePath = Path.Combine(tempRootPath, "users.cache.json");
            File.WriteAllText(sourcePath, "[\"QA User\",\"Operator\"]");

            var loadResult = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });
            Assert.True((bool)(GetProperty(loadResult, "LoadedFromSource") ?? false));
            Assert.False((bool)(GetProperty(loadResult, "LoadedFromCache") ?? true));

            var users = GetUsers(loadResult);
            Assert.Equal(new[] { "QA User", "Operator" }, users);
            Assert.True(File.Exists(cachePath));
        }
        finally
        {
            try
            {
                Directory.Delete(tempRootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup races.
            }
        }
    }

    [Fact]
    public void SR12_UsersDirectory_UsesCache_WhenSourceUnavailable()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_UsersDirectory_SR12", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var sourcePath = Path.Combine(tempRootPath, "missing-users.json");
            var cachePath = Path.Combine(tempRootPath, "users.cache.json");
            File.WriteAllText(cachePath, JsonSerializer.Serialize(new[] { "Cache User 1", "Cache User 2" }));

            var loadResult = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });
            Assert.False((bool)(GetProperty(loadResult, "LoadedFromSource") ?? true));
            Assert.True((bool)(GetProperty(loadResult, "LoadedFromCache") ?? false));

            var users = GetUsers(loadResult);
            Assert.Equal(new[] { "Cache User 1", "Cache User 2" }, users);

            var statusText = (string)(GetProperty(loadResult, "StatusText") ?? string.Empty);
            Assert.Contains("кэш", statusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup races.
            }
        }
    }

    [Fact]
    public void SR13_GroupOrder_ExpandCollapse_ShowsAndHidesItemRows()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1301");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colOrderNumber = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colOrderNumber");

            Assert.Single(GetVisibleRows(dgv));
            Assert.StartsWith("▸", dgv.Rows[0].Cells[colOrderNumber.Index].Value?.ToString(), StringComparison.Ordinal);

            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var expandedRows = GetVisibleRows(dgv);
            Assert.Equal(3, expandedRows.Count);
            Assert.StartsWith("▾", expandedRows[0].Cells[colOrderNumber.Index].Value?.ToString(), StringComparison.Ordinal);
            Assert.StartsWith("item|", expandedRows[1].Tag?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.StartsWith("item|", expandedRows[2].Tag?.ToString() ?? string.Empty, StringComparison.Ordinal);

            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            Assert.Single(GetVisibleRows(dgv));
            Assert.StartsWith("▸", dgv.Rows[0].Cells[colOrderNumber.Index].Value?.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SR14_GroupOrder_BrowseFolderRule_ReturnsMismatchReason_ForDifferentRoots()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder(
                "GR-1401",
                @"C:\Orders\GR-1401\in\front.pdf",
                @"D:\Orders\GR-1401\in\back.pdf");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);

            var method = form.GetType().GetMethod("TryGetBrowseFolderPathForOrder", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(form.GetType().FullName, "TryGetBrowseFolderPathForOrder");

            object[] args = { groupOrder, string.Empty, string.Empty };
            var canBrowse = (bool)(method.Invoke(form, args) ?? false);
            var reason = args[2]?.ToString() ?? string.Empty;

            Assert.False(canBrowse);
            Assert.Equal("Пути не совпадают", reason);
        });
    }

    [Fact]
    public void SR15_GroupOrder_ItemSelection_DisablesAddFile_AndActionsStayAtContainerLevel()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1501");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");
            var addFileButton = MainFormTestHarness.GetPrivateField<ToolStripButton>(form, "tsbAddFile");

            var itemRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal));

            dgv.ClearSelection();
            dgv.CurrentCell = itemRow.Cells[colStatus.Index];
            itemRow.Selected = true;

            MainFormTestHarness.InvokePrivate(form, "UpdateActionButtonsState");
            Assert.False(addFileButton.Enabled);

            var selectedOrderFromItemRow = MainFormTestHarness.InvokePrivate(form, "GetSelectedOrder") as OrderData;
            Assert.NotNull(selectedOrderFromItemRow);
            Assert.Equal(groupOrder.InternalId, selectedOrderFromItemRow!.InternalId);

            var orderRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{groupOrder.InternalId}", StringComparison.Ordinal));

            dgv.ClearSelection();
            dgv.CurrentCell = orderRow.Cells[colStatus.Index];
            orderRow.Selected = true;

            MainFormTestHarness.InvokePrivate(form, "UpdateActionButtonsState");
            Assert.True(addFileButton.Enabled);
        });
    }

    [Fact]
    public void SR16_GroupOrder_ReverseTransition_DemotesToSingle_WhenOneItemRemains()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1601");
            Assert.Equal(2, groupOrder.Items.Count);
            Assert.True(OrderTopologyService.IsMultiOrder(groupOrder));

            var removedItem = groupOrder.Items[0];
            removedItem.SourcePath = string.Empty;
            removedItem.PreparedPath = string.Empty;
            removedItem.PrintPath = string.Empty;

            var removeItemMethod = form.GetType().GetMethod("RemoveItemIfEmpty", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(form.GetType().FullName, "RemoveItemIfEmpty");
            var removed = (bool)(removeItemMethod.Invoke(null, new object[] { groupOrder, removedItem }) ?? false);
            Assert.True(removed);
            Assert.Single(groupOrder.Items);

            var normalizeMethod = form.GetType().GetMethod("NormalizeOrderTopologyAfterItemMutation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(form.GetType().FullName, "NormalizeOrderTopologyAfterItemMutation");
            var demoted = (bool)(normalizeMethod.Invoke(form, new object[] { groupOrder, true, "sr16-remove-item" }) ?? false);

            Assert.True(demoted);
            Assert.False(OrderTopologyService.IsMultiOrder(groupOrder));
            Assert.Equal(OrderFileTopologyMarker.SingleOrder, groupOrder.FileTopologyMarker);
            Assert.Equal(groupOrder.Items[0].SourcePath, groupOrder.SourcePath);
        });
    }

    [Fact]
    public void SR17_GroupOrder_ItemRowSelection_IsDetectedAsItemDeletionTarget_NotOrderContainer()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1701");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");

            var itemRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal));

            dgv.ClearSelection();
            dgv.CurrentCell = itemRow.Cells[colStatus.Index];
            itemRow.Selected = true;

            var hasOrderContainerSelection = (bool)(MainFormTestHarness.InvokePrivate(form, "HasSelectedOrderContainerRow") ?? true);
            Assert.False(hasOrderContainerSelection);

            var selectedOrderItems = MainFormTestHarness.InvokePrivate(form, "GetSelectedOrderItems") as IEnumerable;
            Assert.NotNull(selectedOrderItems);
            Assert.Single(selectedOrderItems!.Cast<object>());
        });
    }

    private static OrderData CreateOrder(string id, string status, string userName, DateTime createdAt, DateTime receivedAt)
    {
        return new OrderData
        {
            InternalId = Guid.NewGuid().ToString("N"),
            Id = id,
            StartMode = OrderStartMode.Simple,
            Status = status,
            UserName = userName,
            OrderDate = createdAt,
            ArrivalDate = receivedAt,
            FolderName = string.Empty
        };
    }

    private static OrderData CreateGroupOrder(
        string id,
        string firstSourcePath = @"C:\Orders\group\in\front.pdf",
        string secondSourcePath = @"C:\Orders\group\in\back.pdf")
    {
        return new OrderData
        {
            InternalId = Guid.NewGuid().ToString("N"),
            Id = id,
            StartMode = OrderStartMode.Extended,
            FileTopologyMarker = OrderFileTopologyMarker.MultiOrder,
            Status = WorkflowStatusNames.Waiting,
            UserName = "QA User",
            OrderDate = new DateTime(2026, 1, 10),
            ArrivalDate = new DateTime(2026, 1, 10),
            FolderName = string.Empty,
            Items =
            [
                new OrderFileItem
                {
                    ItemId = Guid.NewGuid().ToString("N"),
                    ClientFileLabel = "front",
                    SourcePath = firstSourcePath,
                    FileStatus = WorkflowStatusNames.Waiting,
                    SequenceNo = 0
                },
                new OrderFileItem
                {
                    ItemId = Guid.NewGuid().ToString("N"),
                    ClientFileLabel = "back",
                    SourcePath = secondSourcePath,
                    FileStatus = WorkflowStatusNames.Waiting,
                    SequenceNo = 1
                }
            ]
        };
    }

    private static List<DataGridViewRow> GetVisibleRows(DataGridView dgv)
    {
        return dgv.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow && row.Visible)
            .ToList();
    }

    private static void ResetAllFilters(MainForm form)
    {
        var selectedStatuses = MainFormTestHarness.GetPrivateField<HashSet<string>>(form, "_selectedFilterStatuses");
        selectedStatuses.Clear();

        var selectedUsers = MainFormTestHarness.GetPrivateField<HashSet<string>>(form, "_selectedFilterUsers");
        selectedUsers.Clear();

        MainFormTestHarness.SetPrivateField(form, "_orderNumberFilterText", string.Empty);
        MainFormTestHarness.SetPrivateEnumFieldByValue(form, "_createdDateFilterKind", 0); // None
        MainFormTestHarness.SetPrivateEnumFieldByValue(form, "_receivedDateFilterKind", 0); // None

        var queueCombo = MainFormTestHarness.GetPrivateField<ComboBox>(form, "cbQueue");
        if (queueCombo.Items.Count > 0)
            queueCombo.SelectedIndex = 0;

        MainFormTestHarness.InvokePrivate(form, "ApplyStatusFilterToGrid");
    }

    private static string[] GetVisibleOrderIds(MainForm form)
    {
        var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
        var orderNumberColumn = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colOrderNumber");

        var ids = dgv.Rows
            .Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow && row.Visible)
            .Where(row =>
            {
                var rowTag = row.Tag?.ToString() ?? string.Empty;
                return !rowTag.StartsWith("item|", StringComparison.Ordinal);
            })
            .Select(row => row.Cells[orderNumberColumn.Index].Value?.ToString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return ids;
    }

    private static object InvokeUsersDirectoryLoad(string sourcePath, string cachePath, IEnumerable<string> fallbackUsers)
    {
        var usersDirectoryType = typeof(AppSettings).Assembly.GetType("Replica.UsersDirectoryService")
            ?? throw new InvalidOperationException("Type Replica.UsersDirectoryService not found.");
        var loadMethod = usersDirectoryType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(usersDirectoryType.FullName, "Load");

        return loadMethod.Invoke(null, new object[] { sourcePath, cachePath, fallbackUsers })!
               ?? throw new InvalidOperationException("UsersDirectoryService.Load returned null.");
    }

    private static string[] GetUsers(object loadResult)
    {
        var usersValue = GetProperty(loadResult, "Users");
        if (usersValue is not IEnumerable<string> users)
            return Array.Empty<string>();

        return users.ToArray();
    }

    private static object? GetProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return property.GetValue(target);
    }
}
