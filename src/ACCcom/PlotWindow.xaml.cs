using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class PlotWindow : Window
{
    private readonly PlotViewModel _viewModel;
    private bool _renderPending;
    private Brush? _accentBrush;
    private Brush? _gridBrush;
    private Brush? _textBrush;
    private static readonly FontFamily ConsolasFont = new("Consolas");

    public PlotWindow(PlotViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        WindowHelper.AttachWindowState(this, "PlotWindow");

        _viewModel.DataChanged += OnDataChanged;
        SizeChanged += (_, _) => RequestRender();

        Closed += (_, _) => _viewModel.DataChanged -= OnDataChanged;
    }

    private void OnDataChanged()
    {
        // Coalesce rapid updates to avoid UI flood
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _renderPending = false;
            RenderChart();
        });
    }

    private void RequestRender()
    {
        if (!_renderPending)
        {
            _renderPending = true;
            Dispatcher.BeginInvoke(() =>
            {
                _renderPending = false;
                RenderChart();
            });
        }
    }

    private void RenderChart()
    {
        var points = _viewModel.GetSnapshot();

        // Update header info
        LatestValueText.Text = points.Count > 0 ? $"{_viewModel.LatestValue:F4}" : "--";
        PointCountText.Text = string.Format(LanguageManager.Instance["Plot.Points"], points.Count);
        YRangeText.Text = points.Count > 1
            ? $"{_viewModel.MinValue:F2} ~ {_viewModel.MaxValue:F2}"
            : "--";

        if (points.Count < 2)
        {
            StatusText.Text = LanguageManager.Instance["Plot.WaitingForData"];
            HideChart();
            return;
        }

        StatusText.Text = string.Format(LanguageManager.Instance["Plot.Status"], points.Count, _viewModel.MaxValue.ToString("F2"), _viewModel.MinValue.ToString("F2"));

        double canvasW = PlotCanvas.ActualWidth;
        double canvasH = PlotCanvas.ActualHeight;
        if (canvasW < 1 || canvasH < 1) return;

        double yMin = _viewModel.MinValue;
        double yMax = _viewModel.MaxValue;

        // Add 5% padding to Y range
        double yRange = yMax - yMin;
        if (yRange < 1e-9) yRange = 1.0;
        yMin -= yRange * 0.05;
        yMax += yRange * 0.05;
        yRange = yMax - yMin;

        EnsureChartElements();
        ShowChartElements();

        // Grid + labels are created once and updated in place: steady-state
        // renders (one per sample, coalesced) allocate only the PointCollection.
        UpdateGrid(canvasW, canvasH);

        _accentBrush ??= (Brush)FindResource("AccentBrush");
        var polyline = _polyline!;
        int count = points.Count;
        double xStep = canvasW / Math.Max(1, _viewModel.MaxPoints - 1);
        var pc = new PointCollection(count);
        for (int i = 0; i < count; i++)
        {
            double x = i * xStep;
            double y = canvasH - ((points[i].Value - yMin) / yRange) * canvasH;
            pc.Add(new Point(x, y));
        }
        polyline.Points = pc;

        // Draw Y-axis labels
        UpdateYAxisLabels(canvasH, yMin, yMax);
    }

    private Polyline? _polyline;
    private readonly Line?[] _gridLines = new Line?[6];
    private readonly TextBlock?[] _yLabels = new TextBlock?[6];

    private void EnsureChartElements()
    {
        if (_polyline != null) return;

        _accentBrush ??= (Brush)FindResource("AccentBrush");
        _gridBrush ??= (Brush)FindResource("DividerBrush");
        _textBrush ??= (Brush)FindResource("InkTertiaryBrush");

        // Grid first so the polyline lands above it in z-order.
        for (int i = 0; i < _gridLines.Length; i++)
        {
            var line = new Line
            {
                Stroke = _gridBrush,
                StrokeThickness = 0.5,
                StrokeDashArray = DashArray,
            };
            _gridLines[i] = line;
            PlotCanvas.Children.Add(line);
        }

        _polyline = new Polyline
        {
            Stroke = _accentBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        PlotCanvas.Children.Add(_polyline);

        for (int i = 0; i < _yLabels.Length; i++)
        {
            var tb = new TextBlock
            {
                FontSize = 10,
                FontFamily = ConsolasFont,
                Foreground = _textBrush,
            };
            _yLabels[i] = tb;
            YAxisCanvas.Children.Add(tb);
        }
    }

    private static readonly DoubleCollection DashArray = Frozen(new DoubleCollection { 4, 2 });

    private static DoubleCollection Frozen(DoubleCollection c)
    {
        c.Freeze();
        return c;
    }

    private void UpdateGrid(double canvasW, double canvasH)
    {
        int gridLines = _gridLines.Length - 1;
        for (int i = 0; i <= gridLines; i++)
        {
            double y = canvasH * i / gridLines;
            var line = _gridLines[i]!;
            line.X1 = 0;
            line.Y1 = y;
            line.X2 = canvasW;
            line.Y2 = y;
        }
    }

    private void UpdateYAxisLabels(double canvasH, double yMin, double yMax)
    {
        int labelCount = _yLabels.Length - 1;
        double yRange = yMax - yMin;
        for (int i = 0; i <= labelCount; i++)
        {
            double y = canvasH * i / labelCount;
            double value = yMax - (yRange * i / labelCount);
            var tb = _yLabels[i]!;
            tb.Text = value.ToString("F1");
            Canvas.SetLeft(tb, 2);
            Canvas.SetTop(tb, y - 7);
        }
    }

    /// <summary>Blank the chart without discarding the reused elements — used
    /// when there aren't enough points to draw.</summary>
    private void HideChart()
    {
        _polyline?.Points.Clear();
        foreach (var line in _gridLines)
            if (line != null) line.Visibility = Visibility.Collapsed;
        foreach (var tb in _yLabels)
            if (tb != null) tb.Visibility = Visibility.Collapsed;
    }

    private void ShowChartElements()
    {
        foreach (var line in _gridLines)
            if (line != null) line.Visibility = Visibility.Visible;
        foreach (var tb in _yLabels)
            if (tb != null) tb.Visibility = Visibility.Visible;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Clear();
        // Drop the reused elements entirely — Clear is a user-initiated
        // low-frequency action, so rebuilding on next render is fine.
        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        _polyline = null;
        Array.Clear(_gridLines);
        Array.Clear(_yLabels);
        LatestValueText.Text = "--";
        PointCountText.Text = LanguageManager.Instance["Plot.ZeroPoints"];
        YRangeText.Text = "--";
        StatusText.Text = LanguageManager.Instance["Plot.Waiting"];
    }

}
