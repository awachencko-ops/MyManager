using System.Drawing;
using System.Windows.Forms;

namespace Replica;

internal static class OrdersWorkspaceGridStyle
{
    internal const int HorizontalPadding = 10;
    internal const int RowHeight = 42;

    internal static Padding CellPadding => new(HorizontalPadding, 0, HorizontalPadding, 0);

    internal static int SafeRightPadding =>
        HorizontalPadding + SystemInformation.VerticalScrollBarWidth + 4;

    internal static void ConfigureJobsGrid(
        DataGridView grid,
        Color rowBaseBackColor,
        Color rowZebraBackColor,
        Color rowSelectedBackColor,
        Color gridLineColor,
        DataGridViewColumn? statusColumn,
        DataGridViewColumn? orderNumberColumn,
        DataGridViewColumn? prepColumn,
        DataGridViewColumn? pitstopColumn,
        DataGridViewColumn? hotImposingColumn,
        DataGridViewColumn? printColumn,
        DataGridViewColumn? receivedColumn,
        DataGridViewColumn? createdColumn)
    {
        if (grid == null)
            return;

        var cellPadding = CellPadding;
        var rightEdgeSafePadding = SafeRightPadding;

        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
        grid.RowTemplate.Height = RowHeight;
        grid.AllowDrop = true;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        grid.GridColor = gridLineColor;
        grid.DefaultCellStyle.BackColor = rowBaseBackColor;
        grid.RowsDefaultCellStyle.BackColor = rowBaseBackColor;
        grid.AlternatingRowsDefaultCellStyle.BackColor = rowZebraBackColor;
        grid.DefaultCellStyle.SelectionBackColor = rowSelectedBackColor;
        grid.RowsDefaultCellStyle.SelectionBackColor = rowSelectedBackColor;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = rowSelectedBackColor;
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.RowsDefaultCellStyle.SelectionForeColor = Color.Black;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.Black;
        grid.DefaultCellStyle.Padding = cellPadding;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = RowHeight;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.Black;
        grid.ColumnHeadersDefaultCellStyle.Padding = cellPadding;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

        GridStyleHelper.DisableSorting(grid);
        GridStyleHelper.ApplyTextColumnStyle(statusColumn);
        GridStyleHelper.ApplyTextColumnStyle(orderNumberColumn);
        GridStyleHelper.ApplyTextColumnStyle(prepColumn);
        GridStyleHelper.ApplyTextColumnStyle(pitstopColumn);
        GridStyleHelper.ApplyTextColumnStyle(hotImposingColumn);
        GridStyleHelper.ApplyTextColumnStyle(printColumn);
        GridStyleHelper.ApplyNumericColumnStyle(receivedColumn, rightPadding: cellPadding.Right);
        GridStyleHelper.ApplyNumericColumnStyle(createdColumn, rightPadding: rightEdgeSafePadding);
    }
}
