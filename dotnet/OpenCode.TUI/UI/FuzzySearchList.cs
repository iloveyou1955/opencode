using Terminal.Gui;
using System.Collections.Generic;
using System.Linq;

namespace OpenCode.TUI.UI;

public class FuzzySearchList<T> : View
{
    private readonly TextField _searchField;
    private readonly ListView _listView;
    private readonly Label _footer;
    private readonly List<T> _allItems;
    private readonly Func<T, string> _itemSelector;
    private List<T> _filteredItems;

    public event Action<T>? OnItemSelected;
    public event Action? OnCancelled;

    public FuzzySearchList(string title, IEnumerable<T> items, Func<T, string> itemSelector)
    {
        _allItems = items.ToList();
        _filteredItems = new List<T>(_allItems);
        _itemSelector = itemSelector;

        var accentScheme = new ColorScheme()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightCyan)
        };

        var mainScheme = new ColorScheme()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.Black)
        };

        X = Pos.Center();
        Y = Pos.Center();
        Width = 70; // 稍微加宽，更有呼吸感
        Height = 20;
        Border = new Border { 
            BorderStyle = BorderStyle.Single, 
            Title = title,
            Effect3D = false // 禁用 3D 阴影，追求极简扁平化
        };
        ColorScheme = mainScheme;

        _searchField = new TextField("Search...")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill() - 2,
            Height = 1,
            ColorScheme = mainScheme
        };

        _searchField.Enter += (args) => {
            if (_searchField.Text == "Search...") {
                _searchField.Text = "";
                _searchField.ColorScheme = accentScheme;
            }
        };

        _searchField.Leave += (args) => {
            if (string.IsNullOrEmpty(_searchField.Text.ToString())) {
                _searchField.Text = "Search...";
                _searchField.ColorScheme = mainScheme;
            }
        };

        _listView = new ListView(_filteredItems.Select(_itemSelector).ToList())
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 4,
            AllowsMarking = false,
            ColorScheme = new ColorScheme {
                Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
                Focus = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black), // 选中项高亮
                HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
                HotFocus = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        _footer = new Label("Enter: Select | Esc: Cancel")
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill() - 2,
            Height = 1,
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black) }
        };

        _searchField.TextChanged += (oldText) => FilterItems();
        
        _listView.OpenSelectedItem += (args) =>
        {
            if (args.Item >= 0 && args.Item < _filteredItems.Count)
            {
                OnItemSelected?.Invoke(_filteredItems[args.Item]);
            }
        };

        KeyDown += (args) =>
        {
            if (args.KeyEvent.Key == Key.Esc)
            {
                OnCancelled?.Invoke();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.CursorDown && _searchField.HasFocus)
            {
                _listView.SetFocus();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.CursorUp && _listView.HasFocus && _listView.SelectedItem == 0)
            {
                _searchField.SetFocus();
                args.Handled = true;
            }
        };

        Add(_searchField, _listView, _footer);
    }

    private void FilterItems()
    {
        var query = _searchField.Text.ToString()?.ToLower() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            _filteredItems = new List<T>(_allItems);
        }
        else
        {
            _filteredItems = _allItems
                .Where(item => _itemSelector(item).ToLower().Contains(query))
                .ToList();
        }

        _listView.SetSource(_filteredItems.Select(_itemSelector).ToList());
    }
}
