namespace Replica;

// Transitional shim to keep existing tests/tools stable while the form is renamed.
public class MainForm : OrdersWorkspaceForm
{
    public MainForm()
    {
    }

    internal MainForm(ISettingsProvider settingsProvider)
        : base(settingsProvider)
    {
    }
}
