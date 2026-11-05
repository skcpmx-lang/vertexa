using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Merqo.Vertexa.App.ViewModels;

namespace Merqo.Vertexa.App.Views;

/// <summary>
/// Borderless full-screen pattern window. No overlays, no chrome, no cursor: exactly what
/// a panel inspection needs. Arrow keys move through patterns, Escape returns immediately.
/// </summary>
public sealed class DisplayPatternWindow : Window
{
    private readonly DisplayLabViewModel _viewModel;
    private readonly Rectangle _rectangle;

    public DisplayPatternWindow(DisplayLabViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "MERQO VERTEXA — display inspection";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _rectangle = new Rectangle { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Content = new Grid { Background = Brushes.Black, Children = { _rectangle } };

        Loaded += (_, _) => { Apply(); Focus(); Keyboard.Focus(this); };
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += (_, _) => Advance();
        _viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisplayLabViewModel.Index) or nameof(DisplayLabViewModel.CurrentPattern)) Apply();
    }

    private void Advance() => _viewModel.NextCommand.Execute(null);

    private void Apply()
    {
        var pattern = _viewModel.CurrentPattern;
        if (pattern.IsGradient)
        {
            _rectangle.Fill = new LinearGradientBrush(Colors.Black, Colors.White, new Point(0, 0.5), new Point(1, 0.5));
            return;
        }

        if (pattern.IsGrid)
        {
            var group = new DrawingGroup();
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 255, 255)), 1);
            for (var i = 1; i < 16; i++)
            {
                var offset = (double)i / 16;
                group.Children.Add(new GeometryDrawing(null, gridPen,
                    new LineGeometry(new Point(0, 0), new Point(0, 1)) { }));
                _ = offset;
            }
            // A checker border plus centre cross gives an unambiguous sharpness reference.
            var cross = new GeometryGroup();
            cross.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, 1)));
            cross.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(1, 0.5)));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.None,
                Stretch = Stretch.Fill,
                Viewport = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            };
            _rectangle.Fill = new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 255, 255)), 1), cross))
            {
                Stretch = Stretch.Fill,
                Viewport = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                TileMode = TileMode.None,
            };
            _ = brush;
            return;
        }

        _rectangle.Fill = pattern.Solid is null
            ? Brushes.Black
            : (Brush)new BrushConverter().ConvertFromString(pattern.Solid)!;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.Enter:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Home:
                _viewModel.FirstCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
