using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IntegratedModManager.DeveloperTools.Pages;

/// <summary>A page of the tools: labelled again when the language changes, read again when shown or refreshed.</summary>
internal interface IToolPage
{
    void ApplyLanguage();
    Task RefreshAsync();
}

/// <summary>Small pieces the pages build in code, styled with the app's tokens.</summary>
internal static class PageKit
{
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A row: a title with an optional detail line on the left, a value or control on the right.</summary>
    public static Border Row(string title, string? detail, UIElement? right)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (!string.IsNullOrWhiteSpace(detail))
            text.Children.Add(new TextBlock { Text = detail, Style = Style("CaptionTextStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        grid.Children.Add(text);
        if (right is not null)
        {
            Grid.SetColumn((FrameworkElement)right, 1);
            grid.Children.Add(right);
        }
        return new Border { Style = Style("InsetBorderStyle"), Padding = new Thickness(12, 8, 12, 8), Child = grid };
    }

    public static TextBlock Value(string text, FrameworkElement? owner = null, string? brushKey = null)
    {
        var block = new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (owner is not null && brushKey is not null && ThemeBrush(owner, brushKey) is { } brush) block.Foreground = brush;
        return block;
    }

    /// <summary>
    /// A token brush in the theme the element shows (the window may differ from Windows): read from the token
    /// dictionary's theme dictionaries, as the app does.
    /// </summary>
    public static Brush? ThemeBrush(FrameworkElement owner, string key)
    {
        string theme = owner.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out object? found) && found is ResourceDictionary themed
                && themed.TryGetValue(key, out object? brush))
                return brush as Brush;
        return null;
    }

    public static Button Button(string text, RoutedEventHandler click, string style = "SecondaryButtonStyle")
    {
        var button = new Button { Content = text, Style = Style(style), VerticalAlignment = VerticalAlignment.Center };
        button.Click += click;
        return button;
    }
}
