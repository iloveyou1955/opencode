using Terminal.Gui;
using OpenCode.TUI.Services;
using System.Collections.Generic;

namespace OpenCode.TUI.UI;

public class CommandDialog : FuzzySearchList<CommandOption>
{
    public CommandDialog(DialogService dialogService, string title, IEnumerable<CommandOption> options) 
        : base(title, options, o => string.IsNullOrWhiteSpace(o.Description) ? o.Title : $"{o.Title} · {o.Description}")
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
