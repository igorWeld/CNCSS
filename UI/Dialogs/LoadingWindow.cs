using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CNCSS.UI.Dialogs;

/// <summary>
/// Модальное окно загрузки с бесконечным прогрессом.
/// Блокирует взаимодействие с owner и слегка затемняет его.
/// </summary>
public sealed class LoadingWindow : Window
{
    private readonly Window _owner;
    private readonly double _ownerOpacityBefore;
    private readonly bool _ownerEnabledBefore;
    private readonly TextBlock _messageText;

    public LoadingWindow(Window owner, string message, string iconGlyph = "\uE895")
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 360;
        Height = 132;
        Background = new SolidColorBrush(Color.FromRgb(37, 37, 38));

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(95, 95, 98)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 205, 255)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(icon, 0);
        Grid.SetRow(icon, 0);
        root.Children.Add(icon);

        _messageText = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 12)
        };
        Grid.SetColumn(_messageText, 1);
        Grid.SetRow(_messageText, 0);
        root.Children.Add(_messageText);

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 14,
            Minimum = 0,
            Maximum = 100
        };
        Grid.SetColumn(progress, 1);
        Grid.SetRow(progress, 1);
        root.Children.Add(progress);

        border.Child = root;
        Content = border;

        _ownerOpacityBefore = owner.Opacity;
        _ownerEnabledBefore = owner.IsEnabled;
    }

    public void UpdateMessage(string message)
    {
        _messageText.Text = message;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _owner.Opacity = 0.72;
        _owner.IsEnabled = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _owner.IsEnabled = _ownerEnabledBefore;
        _owner.Opacity = _ownerOpacityBefore;
        base.OnClosed(e);
    }
}
