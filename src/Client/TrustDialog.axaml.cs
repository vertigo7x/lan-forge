using Avalonia.Controls;

namespace LanForge.Client;

public partial class TrustDialog : Window
{
    public bool Trusted { get; private set; }

    public TrustDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ConfirmAsync(Window owner, string serverName, string baseUrl, string fingerprint)
    {
        var dialog = new TrustDialog
        {
            MessageText = { Text = $"El servidor '{serverName}' en {baseUrl} aún no es de confianza." },
            FingerprintText = { Text = fingerprint },
        };
        await dialog.ShowDialog(owner);
        return dialog.Trusted;
    }

    private void OnAccept(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Trusted = true;
        Close();
    }

    private void OnReject(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
