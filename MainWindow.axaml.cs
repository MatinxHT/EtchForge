using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EtchForge.ViewModels;

namespace EtchForge;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Closed += (_, _) => viewModel.CancelActiveOperation();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
