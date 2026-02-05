using Terminal.Gui;
using OpenCode.TUI.Services;
using System.Collections.Generic;

namespace OpenCode.TUI.UI;

public class CommandDialog : FuzzySearchList<CommandOption>
{
    public CommandDialog(DialogService dialogService, IEnumerable<CommandOption> options) 
        : base("Command Palette", options, o => o.Title)
    {
        OnItemSelected += (option) =>
        {
            dialogService.CloseDialog();
            option.Action?.Invoke();
        };

        OnCancelled += () => dialogService.CloseDialog();
    }
}

public class CommandOption
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Action? Action { get; set; }
}
