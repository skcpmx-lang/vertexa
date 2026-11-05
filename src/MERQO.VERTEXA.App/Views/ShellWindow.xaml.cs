using System.Windows;
using System.Windows.Input;
using Merqo.Vertexa.App.ViewModels;

namespace Merqo.Vertexa.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => Height = Math.Min(SystemParameters.WorkArea.Height * 0.94, 980);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            shell.IsPaletteOpen = !shell.IsPaletteOpen;
            if (shell.IsPaletteOpen) PaletteInput.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && shell.IsPaletteOpen)
        {
            shell.IsPaletteOpen = false;
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F5)
        {
            if (shell.FullScanCommand.CanExecute(null)) shell.FullScanCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F5)
        {
            if (shell.QuickScanCommand.CanExecute(null)) shell.QuickScanCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PaletteInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        if (e.Key == Key.Enter)
        {
            shell.ExecutePaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PaletteList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.ExecutePaletteCommand.Execute(null);
    }
}
