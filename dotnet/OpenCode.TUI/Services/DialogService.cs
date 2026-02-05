using Terminal.Gui;
using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.TUI.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class DialogService
{
    private readonly Stack<View> _dialogStack = new();
    private View? _currentOverlay;

    public void ShowDialog(View dialogView)
    {
        if (_currentOverlay != null)
        {
            Application.Top.Remove(_currentOverlay);
        }

        _dialogStack.Push(dialogView);
        _currentOverlay = dialogView;
        
        Application.Top.Add(_currentOverlay);
        _currentOverlay.SetFocus();
        Application.Top.SetNeedsDisplay();
    }

    public void CloseDialog()
    {
        if (_currentOverlay != null)
        {
            Application.Top.Remove(_currentOverlay);
            _dialogStack.Pop();
        }

        if (_dialogStack.Count > 0)
        {
            _currentOverlay = _dialogStack.Peek();
            Application.Top.Add(_currentOverlay);
            _currentOverlay.SetFocus();
        }
        else
        {
            _currentOverlay = null;
        }

        Application.Top.SetNeedsDisplay();
    }

    public void Clear()
    {
        while (_dialogStack.Count > 0)
        {
            CloseDialog();
        }
    }

    public bool IsDialogOpen => _dialogStack.Count > 0;
}
