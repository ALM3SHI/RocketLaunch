using System.Windows;
using System.Windows.Controls;

namespace LauncherApp;

/// <summary>
/// A minimal WPF input dialog used as a clean replacement for
/// Microsoft.VisualBasic.Interaction.InputBox — keeps the project a
/// pure WPF project without the Windows Forms dependency.
/// </summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _textBox;

    public string Result { get; private set; } = string.Empty;

    public InputDialog(string prompt, string title = "RocketLaunch")
    {
        Title           = title;
        Width           = 380;
        SizeToContent   = SizeToContent.Height;
        ResizeMode      = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background      = System.Windows.Media.Brushes.Transparent;

        var root = new Border
        {
            Background      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromRgb(0x15, 0x18, 0x24)),
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(20)
        };

        var panel = new StackPanel { Margin = new Thickness(0) };

        panel.Children.Add(new TextBlock
        {
            Text       = prompt,
            Foreground = System.Windows.Media.Brushes.LightGray,
            FontSize   = 12,
            Margin     = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        _textBox = new TextBox
        {
            Background   = new System.Windows.Media.SolidColorBrush(
                               System.Windows.Media.Color.FromRgb(0x1A, 0x1D, 0x27)),
            Foreground   = System.Windows.Media.Brushes.White,
            BorderBrush  = new System.Windows.Media.SolidColorBrush(
                               System.Windows.Media.Color.FromRgb(0x2A, 0x2D, 0x3E)),
            BorderThickness = new Thickness(1),
            Padding      = new Thickness(8, 6, 8, 6),
            CaretBrush   = System.Windows.Media.Brushes.White,
            Margin       = new Thickness(0, 0, 0, 14),
            FontSize     = 12
        };

        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Accept();
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        };

        panel.Children.Add(_textBox);

        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cancelBtn = MakeButton("Cancel", "#2A2D3E");
        cancelBtn.Click += (_, _) => Close();
        Grid.SetColumn(cancelBtn, 0);

        var okBtn = MakeButton("OK", "#4F6BF5");
        okBtn.Click += (_, _) => Accept();
        Grid.SetColumn(okBtn, 2);

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(okBtn);
        panel.Children.Add(buttonRow);

        root.Child  = panel;
        Content     = root;
        AllowsTransparency = false;

        Loaded += (_, _) => _textBox.Focus();
    }

    private void Accept()
    {
        Result = _textBox.Text.Trim();
        DialogResult = true;
    }

    private static Button MakeButton(string text, string hexColor)
    {
        var c = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hexColor);

        return new Button
        {
            Content         = text,
            Background      = new System.Windows.Media.SolidColorBrush(c),
            Foreground      = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(12, 7, 12, 7),
            Cursor          = System.Windows.Input.Cursors.Hand,
            FontSize        = 12
        };
    }

    /// <summary>
    /// Static helper that matches the familiar InputBox(prompt, title) pattern.
    /// Returns empty string if the user cancelled.
    /// </summary>
    public static string Show(string prompt, string title = "RocketLaunch",
                              Window? owner = null)
    {
        var dlg = new InputDialog(prompt, title);
        if (owner is not null) dlg.Owner = owner;
        return dlg.ShowDialog() == true ? dlg.Result : string.Empty;
    }
}
