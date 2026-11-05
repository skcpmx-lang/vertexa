using System.Windows.Controls;
using System.Windows.Input;
using Merqo.Vertexa.App.ViewModels;

namespace Merqo.Vertexa.App.Views;

public partial class KeyboardLabView : UserControl
{
    public KeyboardLabView() => InitializeComponent();

    private void KeyCaptureSurface_MouseDown(object sender, MouseButtonEventArgs e) => KeyCaptureSurface.Focus();

    private void KeyCaptureSurface_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not KeyboardLabViewModel viewModel) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        viewModel.RegisterKeyDown(virtualKey.ToString(), key.ToString());
        e.Handled = key is Key.Tab;
    }

    private void KeyCaptureSurface_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (DataContext is not KeyboardLabViewModel viewModel) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        viewModel.RegisterKeyUp(KeyInterop.VirtualKeyFromKey(key).ToString());
    }
}
