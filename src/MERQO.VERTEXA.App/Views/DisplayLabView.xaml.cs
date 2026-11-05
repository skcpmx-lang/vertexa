using System.Windows;
using System.Windows.Controls;
using Merqo.Vertexa.App.ViewModels;

namespace Merqo.Vertexa.App.Views;

public partial class DisplayLabView : UserControl
{
    public DisplayLabView() => InitializeComponent();

    private void StartInspection_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DisplayLabViewModel viewModel) return;
        var window = new DisplayPatternWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
