using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EtchForge.ViewModels;

namespace EtchForge;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        Closed += (_, _) =>
        {
            viewModel.CloseRequested -= Close;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
