using Avalonia.Controls;
using LanForge.Client.ViewModels;

namespace LanForge.Client.Controls;

public partial class GameCard : UserControl
{
    public GameCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameCardItem item)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(GameCardItem.ProgressPercent))
                    {
                        UpdateProgress();
                    }
                    else if (e.PropertyName is nameof(GameCardItem.State) or nameof(GameCardItem.IsPlayAction))
                    {
                        UpdateActionTheme();
                    }
                };
            }

            UpdateProgress();
            UpdateActionTheme();
        };
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
    }

    /// <summary>Variante "installed" del design: la acción principal pasa a accent (Jugar).</summary>
    private void UpdateActionTheme()
    {
        var key = DataContext is GameCardItem { IsPlayAction: true } ? "ButtonPrimary" : "ButtonSecondary";
        ActionButton.Theme = (Avalonia.Styling.ControlTheme)this.Resources[key]!;
    }

    private void UpdateProgress()
    {
        if (DataContext is not GameCardItem item)
        {
            return;
        }

        PercentText.Text = $"{item.ProgressPercent}%";
        ProgressFill.Width = ProgressTrack.Bounds.Width * item.ProgressPercent / 100.0;
    }
}
