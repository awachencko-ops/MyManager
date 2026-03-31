using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Replica;

internal static class GridStyleHelper
{
    internal const int DefaultHorizontalPadding = 10;

    internal static Padding HorizontalPadding =>
        new(DefaultHorizontalPadding, 0, DefaultHorizontalPadding, 0);

    internal static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        if (grid == null)
            return;

        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
    }

    internal static void HideColumnsExcept(DataGridView grid, params string[] visibleColumnNames)
    {
        if (grid == null)
            return;

        var visibleNames = new HashSet<string>(visibleColumnNames ?? [], System.StringComparer.Ordinal);
        foreach (DataGridViewColumn column in grid.Columns)
            column.Visible = visibleNames.Contains(column.Name);
    }

    internal static void DisableSorting(DataGridView grid)
    {
        if (grid == null)
            return;

        foreach (DataGridViewColumn column in grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    internal static void ApplyTextColumnStyle(
        DataGridViewColumn? column,
        string? headerText = null,
        bool fill = false,
        int leftPadding = DefaultHorizontalPadding,
        int rightPadding = DefaultHorizontalPadding)
    {
        ApplyColumnStyle(column, DataGridViewContentAlignment.MiddleLeft, headerText, fill, leftPadding, rightPadding);
    }

    internal static void ApplyNumericColumnStyle(
        DataGridViewColumn? column,
        string? headerText = null,
        bool fill = false,
        int leftPadding = DefaultHorizontalPadding,
        int rightPadding = DefaultHorizontalPadding)
    {
        ApplyColumnStyle(column, DataGridViewContentAlignment.MiddleRight, headerText, fill, leftPadding, rightPadding);
    }

    private static void ApplyColumnStyle(
        DataGridViewColumn? column,
        DataGridViewContentAlignment alignment,
        string? headerText,
        bool fill,
        int leftPadding,
        int rightPadding)
    {
        if (column == null)
            return;

        if (headerText != null)
            column.HeaderText = headerText;

        if (fill)
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        var padding = new Padding(leftPadding, 0, rightPadding, 0);
        column.DefaultCellStyle.Alignment = alignment;
        column.DefaultCellStyle.Padding = padding;
        column.HeaderCell.Style.Alignment = alignment;
        column.HeaderCell.Style.Padding = padding;
    }
}
