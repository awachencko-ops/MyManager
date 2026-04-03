using System.Drawing;
using System.Windows.Forms;

namespace Replica
{
    public partial class OrdersWorkspaceForm
    {
        private void InitializeMainToolbarIcons()
        {
            ApplyToolStripIcon(tsbNewJob, "content", "add_box", ("content", "add"));
            ApplyToolStripIcon(tsbRun, "av", "play_arrow", ("av", "play_circle"));
            ApplyToolStripIcon(tsbStop, "av", "stop", ("av", "stop_circle"));
            ApplyToolStripIcon(tsbRemove, "action", "delete", ("action", "delete_outline"));
            ApplyToolStripIcon(tsbAddFile, "file", "attach_file", ("file", "note_add"));
            ApplyToolStripIcon(tsbBrowse, "file", "folder_open", ("file", "folder"));
            ApplyToolStripIcon(tsbConsole, "action", "terminal", ("action", "article"));
            ApplyToolStripIcon(toolStripButton1, "action", "settings", ("action", "tune"));

            ApplyButtonIcon(btnViewTiles, "action", "grid_view", ("action", "dashboard"));
            ApplyButtonIcon(btnViewList, "action", "view_headline", ("action", "table_rows"));
        }

        private static void ApplyToolStripIcon(ToolStripItem item, string iconFolder, string iconHint, params (string Folder, string FileNameHint)[] fallbacks)
        {
            if (item == null)
                return;

            var icon = OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, iconHint, size: 20, fallbacks);
            if (icon == null)
                icon = new Bitmap(SystemIcons.Application.ToBitmap(), new Size(20, 20));

            item.Image = icon;
            item.ImageScaling = ToolStripItemImageScaling.None;
        }

        private static void ApplyButtonIcon(Button button, string iconFolder, string iconHint, params (string Folder, string FileNameHint)[] fallbacks)
        {
            if (button == null)
                return;

            var icon = OrdersWorkspaceIconCatalog.LoadIcon(iconFolder, iconHint, size: 18, fallbacks);
            if (icon == null)
                icon = new Bitmap(SystemIcons.Application.ToBitmap(), new Size(18, 18));

            button.Image = icon;
        }
    }
}
