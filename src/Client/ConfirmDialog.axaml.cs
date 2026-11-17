using Avalonia.Controls;

namespace LanForge.Client;

/// <summary>Diálogo de confirmación genérico con el tema del design.</summary>
public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, string acceptText)
    {
        var dialog = new ConfirmDialog
        {
            TitleText = { Text = title },
            MessageText = { Text = message },
            AcceptButton = { Content = acceptText },
        };
        await dialog.ShowDialog(owner);
        return dialog.Confirmed;
    }

    private void OnAccept(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
