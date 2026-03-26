using System.Collections;
using System.Drawing;
using System.IO;
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
    public void SR10_UsersDirectory_LoadsDisplayAndServerNames()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_UsersDirectory_SR10", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var sourcePath = Path.Combine(tempRootPath, "users.json");
            var cachePath = Path.Combine(tempRootPath, "users.cache.json");
            File.WriteAllText(
                sourcePath,
                """
                [
                  { "displayName": "Андрей", "serverName": "Andrew" },
                  { "displayName": "Оператор 2", "serverName": "operator-2" }
                ]
                """);

            var loadResult = InvokeUsersDirectoryLoad(sourcePath, cachePath, new[] { "Fallback User" });

            Assert.Equal(new[] { "Андрей", "Оператор 2" }, GetUsers(loadResult));
            var serverUsers = GetServerUsers(loadResult);
            Assert.Equal("Andrew", serverUsers["Андрей"]);
            Assert.Equal("operator-2", serverUsers["Оператор 2"]);
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
    public void SR12A_ResolveLanApiActor_UsesMappedServerUserName()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            MainFormTestHarness.SetPrivateField(form, "_currentUserName", "Андрей");
            var serverUsers = MainFormTestHarness.GetPrivateField<Dictionary<string, string>>(form, "_serverUsersByDisplayName");
            serverUsers.Clear();
            serverUsers["Андрей"] = "Andrew";

            var actor = (string?)MainFormTestHarness.InvokePrivate(form, "ResolveLanApiActor");

            Assert.Equal("Andrew", actor);
        });
    }

    [Fact]
    public void SR12B_UserProfilePanel_ShowsCurrentUserAndRole()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            MainFormTestHarness.InvokePrivate(form, "ApplyCurrentUserProfile", "Andrew", "Администратор");

            var splitUser = MainFormTestHarness.GetPrivateField<SplitContainer>(form, "splitUser");
            var nameLabel = Assert.IsType<Label>(splitUser.Panel2.Controls.Find("lblUserProfileName", true).Single());
            var roleLabel = Assert.IsType<Label>(splitUser.Panel2.Controls.Find("lblUserProfileRole", true).Single());
            var picture = MainFormTestHarness.GetPrivateField<PictureBox>(form, "pictureBox5");

            Assert.Equal("Andrew", nameLabel.Text);
            Assert.Equal("Администратор", roleLabel.Text);
            Assert.NotNull(picture.Image);
        });
    }

    [Fact]
    public void SR13_GroupOrder_ExpandCollapse_ShowsAndHidesItemRows()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1301");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");
            var colOrderNumber = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colOrderNumber");
            var colReceived = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colReceived");
            var colCreated = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colCreated");
            var expectedOrderDate = groupOrder.OrderDate.ToString("dd.MM.yyyy");
            var expectedArrivalDate = groupOrder.ArrivalDate.ToString("dd.MM.yyyy");

            Assert.Single(GetVisibleRows(dgv));
            Assert.Equal(WorkflowStatusNames.Group, dgv.Rows[0].Cells[colStatus.Index].Value?.ToString());
            Assert.Equal(groupOrder.Id, dgv.Rows[0].Cells[colOrderNumber.Index].Value?.ToString());
            Assert.Equal(expectedOrderDate, dgv.Rows[0].Cells[colReceived.Index].Value?.ToString());
            Assert.Equal(expectedArrivalDate, dgv.Rows[0].Cells[colCreated.Index].Value?.ToString());

            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var expandedRows = GetVisibleRows(dgv);
            Assert.Equal(3, expandedRows.Count);
            Assert.Equal(WorkflowStatusNames.Group, expandedRows[0].Cells[colStatus.Index].Value?.ToString());
            Assert.Equal(groupOrder.Id, expandedRows[0].Cells[colOrderNumber.Index].Value?.ToString());
            Assert.Equal(expectedOrderDate, expandedRows[0].Cells[colReceived.Index].Value?.ToString());
            Assert.Equal(expectedArrivalDate, expandedRows[0].Cells[colCreated.Index].Value?.ToString());
            Assert.Equal(groupOrder.Id, expandedRows[1].Cells[colOrderNumber.Index].Value?.ToString());
            Assert.Equal(groupOrder.Id, expandedRows[2].Cells[colOrderNumber.Index].Value?.ToString());
            Assert.Equal(expectedOrderDate, expandedRows[1].Cells[colReceived.Index].Value?.ToString());
            Assert.Equal(expectedOrderDate, expandedRows[2].Cells[colReceived.Index].Value?.ToString());
            Assert.Equal(expectedArrivalDate, expandedRows[1].Cells[colCreated.Index].Value?.ToString());
            Assert.Equal(expectedArrivalDate, expandedRows[2].Cells[colCreated.Index].Value?.ToString());
            Assert.StartsWith("item|", expandedRows[1].Tag?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.StartsWith("item|", expandedRows[2].Tag?.ToString() ?? string.Empty, StringComparison.Ordinal);

            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            Assert.Single(GetVisibleRows(dgv));
            Assert.Equal(groupOrder.Id, dgv.Rows[0].Cells[colOrderNumber.Index].Value?.ToString());
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

            var method = MainFormTestHarness.GetPrivateMethod(
                form,
                "TryGetBrowseFolderPathForOrder",
                BindingFlags.Instance);

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

            var removeItemMethod = MainFormTestHarness.GetPrivateMethod(
                form,
                "RemoveItemIfEmpty",
                BindingFlags.Static);
            var removed = (bool)(removeItemMethod.Invoke(null, new object[] { groupOrder, removedItem }) ?? false);
            Assert.True(removed);
            Assert.Single(groupOrder.Items);

            var normalizeMethod = MainFormTestHarness.GetPrivateMethod(
                form,
                "NormalizeOrderTopologyAfterItemMutation",
                BindingFlags.Instance);
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

    [Fact]
    public void SR18_GroupOrder_HeaderRow_DoesNotMirrorFirstItemStageFields()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1801");
            groupOrder.Items[0].PreparedPath = @"C:\Orders\group\prepress\front-prepared.pdf";
            groupOrder.Items[0].PrintPath = @"C:\Orders\group\print\front-print.pdf";
            groupOrder.PitStopAction = "Outlines CMYK";
            groupOrder.ImposingAction = "Step & Repeat";

            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colPrep = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colPrep");
            var colPitstop = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colPitstop");
            var colHotimposing = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colHotimposing");
            var colPrint = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colPrint");

            var orderRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{groupOrder.InternalId}", StringComparison.Ordinal));

            Assert.Equal("-", orderRow.Cells[colPrep.Index].Value?.ToString());
            Assert.Equal("-", orderRow.Cells[colPrint.Index].Value?.ToString());
            Assert.Equal("Outlines CMYK", orderRow.Cells[colPitstop.Index].Value?.ToString());
            Assert.Equal("Step & Repeat", orderRow.Cells[colHotimposing.Index].Value?.ToString());

            var isPreparedLocked = (bool)(MainFormTestHarness.InvokePrivate(
                form,
                "IsGroupContainerFileStageLocked",
                groupOrder,
                OrderStages.Prepared) ?? false);
            var isPrintLocked = (bool)(MainFormTestHarness.InvokePrivate(
                form,
                "IsGroupContainerFileStageLocked",
                groupOrder,
                OrderStages.Print) ?? false);
            Assert.True(isPreparedLocked);
            Assert.True(isPrintLocked);
        });
    }

    [Fact]
    public void SR19_GroupOrder_ItemContext_ResolvesOrderAndItem()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-1901");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var itemRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal));

            MainFormTestHarness.SetPrivateField(form, "_ctxRow", itemRow.Index);
            MainFormTestHarness.SetPrivateField(form, "_ctxCol", 0);

            var tryGetContextItemMethod = MainFormTestHarness.GetPrivateMethod(
                form,
                "TryGetContextOrderItem",
                BindingFlags.Instance);

            object?[] args = { null, null };
            var resolved = (bool)(tryGetContextItemMethod.Invoke(form, args) ?? false);

            Assert.True(resolved);
            Assert.NotNull(args[0]);
            Assert.NotNull(args[1]);
            Assert.Equal(groupOrder.InternalId, ((OrderData)args[0]!).InternalId);
        });
    }

    [Fact]
    public void SR20_GroupOrder_ItemRows_UseLightFolderZebraPalette()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-2001");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");

            var itemRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal));

            dgv.ClearSelection();

            var args = new DataGridViewCellFormattingEventArgs(
                colStatus.Index,
                itemRow.Index,
                itemRow.Cells[colStatus.Index].Value,
                typeof(string),
                new DataGridViewCellStyle());
            MainFormTestHarness.InvokePrivate(form, "DgvJobs_CellFormatting", null, args);

            var expectedFieldName = itemRow.Index % 2 == 0
                ? "GroupOrderItemRowBaseBackColor"
                : "GroupOrderItemRowZebraBackColor";
            var expectedField = MainFormTestHarness.GetPrivateFieldInfo(
                form,
                expectedFieldName,
                BindingFlags.Static);
            var expectedColor = (Color)(expectedField.GetValue(null) ?? Color.Empty);

            Assert.NotNull(args.CellStyle);
            Assert.Equal(expectedColor, args.CellStyle!.BackColor);
        });
    }

    [Fact]
    public void SR23_GroupOrder_DragDrop_BetweenItems_CopiesWithoutClearingSource()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var tempRootPath = MainFormTestHarness.GetPrivateField<string>(form, "_ordersRootPath");
            var sourceFile = Path.Combine(tempRootPath, "group-source-a.pdf");
            var targetFile = Path.Combine(tempRootPath, "group-source-b.pdf");
            File.WriteAllText(sourceFile, "source-a");
            File.WriteAllText(targetFile, "source-b");
            Assert.True(File.Exists(targetFile));

            var groupOrder = CreateGroupOrder("GR-2101", sourceFile, targetFile);
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colSource = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colSource");
            var itemRows = dgv.Rows
                .Cast<DataGridViewRow>()
                .Where(row => (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, itemRows.Count);

            var dragData = new DataObject();
            dragData.SetData(DataFormats.FileDrop, new[] { sourceFile });
            dragData.SetData("InternalSourceRow", itemRows[0].Index);
            dragData.SetData("InternalSourceColumn", colSource.Index);

            var task = (Task?)MainFormTestHarness.InvokePrivate(
                form,
                "HandleGridFileDropAsync",
                dragData,
                itemRows[1].Index,
                colSource.Index);
            WaitForTask(task);

            Assert.True(File.Exists(sourceFile));
            Assert.Equal(sourceFile, groupOrder.Items[0].SourcePath);
            Assert.True(File.Exists(groupOrder.Items[1].SourcePath));
            Assert.NotEqual(sourceFile, groupOrder.Items[1].SourcePath);
            Assert.True(OrderTopologyService.IsMultiOrder(groupOrder));
        });
    }

    [Fact]
    public void SR24_GroupOrder_DragDrop_ToSingleOrder_MovesSourceAndClearsOrigin()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var tempRootPath = MainFormTestHarness.GetPrivateField<string>(form, "_ordersRootPath");
            var sourceFile = Path.Combine(tempRootPath, "group-move-a.pdf");
            var remainingFile = Path.Combine(tempRootPath, "group-keep-b.pdf");
            File.WriteAllText(sourceFile, "move-a");
            File.WriteAllText(remainingFile, "keep-b");

            var groupOrder = CreateGroupOrder("GR-2201", sourceFile, remainingFile);
            var targetOrder = CreateOrder("SR-2201", WorkflowStatusNames.Waiting, "QA User", new DateTime(2026, 1, 11), new DateTime(2026, 1, 11));
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", targetOrder);
            MainFormTestHarness.InvokePrivate(form, "ToggleOrderExpanded", groupOrder.InternalId);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colSource = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colSource");
            var sourceItemRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row =>
                    (row.Tag?.ToString() ?? string.Empty).StartsWith("item|", StringComparison.Ordinal)
                    && string.Equals(row.Cells[colSource.Index].Value?.ToString(), Path.GetFileName(sourceFile), StringComparison.OrdinalIgnoreCase));
            var targetOrderRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{targetOrder.InternalId}", StringComparison.Ordinal));

            var dragData = new DataObject();
            dragData.SetData(DataFormats.FileDrop, new[] { sourceFile });
            dragData.SetData("InternalSourceRow", sourceItemRow.Index);
            dragData.SetData("InternalSourceColumn", colSource.Index);

            var task = (Task?)MainFormTestHarness.InvokePrivate(
                form,
                "HandleGridFileDropAsync",
                dragData,
                targetOrderRow.Index,
                colSource.Index);
            WaitForTask(task);

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(remainingFile));
            var targetDisplayPath = (string?)MainFormTestHarness.InvokePrivate(
                form,
                "ResolveSingleOrderDisplayPath",
                targetOrder,
                OrderStages.Source);
            Assert.False(string.IsNullOrWhiteSpace(targetDisplayPath));
            Assert.True(File.Exists(targetDisplayPath));
            Assert.True(OrderTopologyService.IsMultiOrder(groupOrder));
            Assert.Equal(sourceFile, groupOrder.Items[0].SourcePath);
            Assert.Equal(remainingFile, groupOrder.Items[1].SourcePath);
        });
    }

    [Fact]
    public void SR25_MissingFileCell_ShowsRedText_WithoutChangingBackground()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, tempRootPath) =>
        {
            var missingSourcePath = Path.Combine(tempRootPath, "missing-input.pdf");
            var order = CreateOrder("SR-2501", WorkflowStatusNames.Waiting, "QA User", new DateTime(2026, 1, 12), new DateTime(2026, 1, 12));
            order.SourcePath = missingSourcePath;
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", order);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colSource = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colSource");
            var row = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{order.InternalId}", StringComparison.Ordinal));

            var args = new DataGridViewCellFormattingEventArgs(
                colSource.Index,
                row.Index,
                row.Cells[colSource.Index].Value,
                typeof(string),
                new DataGridViewCellStyle());
            MainFormTestHarness.InvokePrivate(form, "DgvJobs_CellFormatting", null, args);

            Assert.NotNull(args.CellStyle);
            Assert.Equal(Color.Firebrick, args.CellStyle!.ForeColor);
            Assert.NotEqual(Color.Firebrick, args.CellStyle.BackColor);
        });
    }

    [Fact]
    public void SR26_QuiteCleanup_RemovesHotfolderArtifacts()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_QHI_Cleanup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var cfg = new ImposingConfig
            {
                BaseFolder = tempRootPath,
                In = Path.Combine(tempRootPath, "in"),
                Out = Path.Combine(tempRootPath, "out"),
                Done = Path.Combine(tempRootPath, "done"),
                Error = Path.Combine(tempRootPath, "error")
            };

            foreach (var folder in new[] { cfg.In, cfg.Out, cfg.Done, cfg.Error })
                Directory.CreateDirectory(folder);

            var fileName = "105x148_упокоение.pdf";
            foreach (var folder in new[] { cfg.In, cfg.Out, cfg.Done, cfg.Error })
            {
                File.WriteAllText(Path.Combine(folder, fileName), folder);
                File.WriteAllText(Path.Combine(folder, fileName + ".log"), folder + ".log");
            }

            var method = typeof(OrderProcessor).GetMethod(
                "CleanupQuiteImposingArtifacts",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var processor = new OrderProcessor(tempRootPath);
            method!.Invoke(processor, new object?[] { cfg, fileName, Array.Empty<string?>() });

            foreach (var folder in new[] { cfg.In, cfg.Out, cfg.Done, cfg.Error })
            {
                Assert.False(File.Exists(Path.Combine(folder, fileName)));
                Assert.False(File.Exists(Path.Combine(folder, fileName + ".log")));
            }
        }
        finally
        {
            if (Directory.Exists(tempRootPath))
                Directory.Delete(tempRootPath, recursive: true);
        }
    }

    [Fact]
    public void SR27_PitStopCleanup_RemovesHotfolderArtifacts()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), "Replica_PIT_Cleanup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var cfg = new ActionConfig
            {
                BaseFolder = tempRootPath,
                InputFolder = Path.Combine(tempRootPath, "Input Folder"),
                ReportSuccess = Path.Combine(tempRootPath, "Reports on Success"),
                ReportError = Path.Combine(tempRootPath, "Reports on Error"),
                OriginalSuccess = Path.Combine(tempRootPath, "Original Docs on Success"),
                OriginalError = Path.Combine(tempRootPath, "Original Docs on Error"),
                ProcessedSuccess = Path.Combine(tempRootPath, "Processed Docs on Success"),
                ProcessedError = Path.Combine(tempRootPath, "Processed Docs on Error")
            };

            foreach (var folder in new[]
            {
                cfg.InputFolder,
                cfg.ReportSuccess,
                cfg.ReportError,
                cfg.OriginalSuccess,
                cfg.OriginalError,
                cfg.ProcessedSuccess,
                cfg.ProcessedError
            })
                Directory.CreateDirectory(folder);

            var fileName = "105x148_упокоение.pdf";
            var reportName = Path.GetFileNameWithoutExtension(fileName) + "_log.pdf";

            foreach (var folder in new[]
            {
                cfg.InputFolder,
                cfg.OriginalSuccess,
                cfg.OriginalError,
                cfg.ProcessedSuccess,
                cfg.ProcessedError
            })
            {
                File.WriteAllText(Path.Combine(folder, fileName), folder);
            }

            File.WriteAllText(Path.Combine(cfg.ReportSuccess, reportName), "report");
            File.WriteAllText(Path.Combine(cfg.ReportError, reportName), "report");

            var method = typeof(OrderProcessor).GetMethod(
                "CleanupPitStopArtifacts",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var processor = new OrderProcessor(tempRootPath);
            method!.Invoke(processor, new object?[] { cfg, fileName, Array.Empty<string?>() });

            foreach (var folder in new[]
            {
                cfg.InputFolder,
                cfg.OriginalSuccess,
                cfg.OriginalError,
                cfg.ProcessedSuccess,
                cfg.ProcessedError
            })
            {
                Assert.False(File.Exists(Path.Combine(folder, fileName)));
            }

            Assert.False(File.Exists(Path.Combine(cfg.ReportSuccess, reportName)));
            Assert.False(File.Exists(Path.Combine(cfg.ReportError, reportName)));
        }
        finally
        {
            if (Directory.Exists(tempRootPath))
                Directory.Delete(tempRootPath, recursive: true);
        }
    }

    [Fact]
    public void SR21_GroupStatus_IsTechnicalAndHiddenFromUiFilters()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            Assert.DoesNotContain(WorkflowStatusNames.Group, WorkflowStatusNames.Filterable);
            Assert.Equal(WorkflowStatusNames.Waiting, WorkflowStatusNames.Normalize(WorkflowStatusNames.Group));

            var folderOrder = CreateOrder("SR21-001", WorkflowStatusNames.Group, "QA User", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1));
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", folderOrder);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");
            var orderRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{folderOrder.InternalId}", StringComparison.Ordinal));

            Assert.Equal(WorkflowStatusNames.Waiting, orderRow.Cells[colStatus.Index].Value?.ToString());
        });
    }

    [Fact]
    public void SR22_GroupOrder_RowClick_TogglesOnlyWhenRowWasAlreadySelected()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var groupOrder = CreateGroupOrder("GR-2201");
            MainFormTestHarness.InvokePrivate(form, "AddCreatedOrder", groupOrder);

            var dgv = MainFormTestHarness.GetPrivateField<DataGridView>(form, "dgvJobs");
            var colStatus = MainFormTestHarness.GetPrivateField<DataGridViewColumn>(form, "colStatus");
            var orderRow = dgv.Rows
                .Cast<DataGridViewRow>()
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{groupOrder.InternalId}", StringComparison.Ordinal));

            dgv.ClearSelection();
            dgv.CurrentCell = orderRow.Cells[colStatus.Index];
            orderRow.Selected = true;

            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowIndex", orderRow.Index);
            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowWasSelected", false);
            MainFormTestHarness.InvokePrivate(form, "DgvJobs_CellClick", null, new DataGridViewCellEventArgs(colStatus.Index, orderRow.Index));
            Assert.Single(GetVisibleRows(dgv));

            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowIndex", orderRow.Index);
            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowWasSelected", true);
            MainFormTestHarness.InvokePrivate(form, "DgvJobs_CellClick", null, new DataGridViewCellEventArgs(colStatus.Index, orderRow.Index));
            Assert.Equal(3, GetVisibleRows(dgv).Count);

            var expandedOrderRow = GetVisibleRows(dgv)
                .First(row => string.Equals(row.Tag?.ToString(), $"order|{groupOrder.InternalId}", StringComparison.Ordinal));
            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowIndex", expandedOrderRow.Index);
            MainFormTestHarness.SetPrivateField(form, "_gridMouseDownRowWasSelected", true);
            MainFormTestHarness.InvokePrivate(form, "DgvJobs_CellClick", null, new DataGridViewCellEventArgs(colStatus.Index, expandedOrderRow.Index));
            Assert.Single(GetVisibleRows(dgv));
        });
    }

    [Fact]
    public void SR23_ActionButtons_NewOrderButton_ReEnabledAfterServerHardLockIsRemoved()
    {
        MainFormTestHarness.RunWithIsolatedForm((form, _) =>
        {
            var newOrderButton = MainFormTestHarness.GetPrivateField<ToolStripButton>(form, "tsbNewJob");

            MainFormTestHarness.SetPrivateField(form, "_serverHardLockActive", true);
            MainFormTestHarness.InvokePrivate(form, "UpdateActionButtonsState");
            Assert.False(newOrderButton.Enabled);

            MainFormTestHarness.SetPrivateField(form, "_serverHardLockActive", false);
            MainFormTestHarness.InvokePrivate(form, "UpdateActionButtonsState");
            Assert.True(newOrderButton.Enabled);
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

    private static void WaitForTask(Task? task, int timeoutMs = 30000)
    {
        if (task == null)
            return;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Task did not complete within {timeoutMs} ms.");

            Application.DoEvents();
            Thread.Sleep(10);
        }

        task.GetAwaiter().GetResult();
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

    private static Dictionary<string, string> GetServerUsers(object loadResult)
    {
        var usersValue = GetProperty(loadResult, "ServerUsersByDisplayName");
        if (usersValue is not IEnumerable serverUsers)
            return [];

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in serverUsers)
        {
            var itemType = item?.GetType();
            if (itemType == null)
                continue;

            var keyProperty = itemType.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
            var valueProperty = itemType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            var key = keyProperty?.GetValue(item) as string;
            var value = valueProperty?.GetValue(item) as string;
            if (!string.IsNullOrWhiteSpace(key) && value != null)
                result[key] = value;
        }

        return result;
    }

    private static object? GetProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return property.GetValue(target);
    }
}

