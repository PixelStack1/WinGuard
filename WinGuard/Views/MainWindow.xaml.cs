using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinGuard.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateChromeForState();
    }

    private void UpdateChromeForState()
    {
        bool max = WindowState == WindowState.Maximized;
        // Borderless maximized windows overflow the work area by the resize frame; inset and square the corners.
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        RootBorder.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(18);
        MaxButton.Content = max ? "\uE923" : "\uE922"; // Restore / Maximize glyphs
        UpdateClip();
    }

    // Clip content to the rounded corners so child backgrounds never poke out.
    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip();

    private void UpdateClip()
    {
        var r = RootBorder.CornerRadius.TopLeft;
        var w = RootBorder.ActualWidth; var h = RootBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;
        RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WindowChrome's caption normally handles drag/double-click natively; this is a fallback.
        if (e.ClickCount == 2) { Maximize_Click(sender, e); return; }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Click on the row's "i": toggles its detailed tooltip (hover also shows it).</summary>
    private void InfoIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ToolTip is System.Windows.Controls.ToolTip tt)
        {
            tt.PlacementTarget = fe;
            tt.IsOpen = !tt.IsOpen;
            e.Handled = true;
        }
    }
}
