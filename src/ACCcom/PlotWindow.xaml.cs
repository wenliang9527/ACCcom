using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class PlotWindow : Window
{
    private readonly PlotViewModel _viewModel;
    private bool _renderPending;

    public PlotWindow(PlotViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

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
        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();

        // Update header info
        LatestValueText.Text = points.Count > 0 ? $"{_viewModel.LatestValue:F4}" : "--";
        PointCountText.Text = $"  ({points.Count} points)";
        YRangeText.Text = points.Count > 1
            ? $"{_viewModel.MinValue:F2} ~ {_viewModel.MaxValue:F2}"
            : "--";

        if (points.Count < 2)
        {
            StatusText.Text = "Waiting for data (need at least 2 points)...";
            return;
        }

        StatusText.Text = $"Plotting {points.Count} points | {_viewModel.MaxValue:F2} max | {_viewModel.MinValue:F2} min";

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

        // Draw grid lines
        DrawGrid(canvasW, canvasH, yMin, yMax, yRange);

        // Build polyline points
        var polyline = new Polyline
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };

        int count = points.Count;
        double xStep = canvasW / Math.Max(1, _viewModel.MaxPoints - 1);

        for (int i = 0; i < count; i++)
        {
            double x = i * xStep;
            double y = canvasH - ((points[i].Value - yMin) / yRange) * canvasH;
            polyline.Points.Add(new Point(x, y));
        }

        PlotCanvas.Children.Add(polyline);

        // Draw Y-axis labels
        DrawYAxisLabels(canvasH, yMin, yMax);
    }

    private void DrawGrid(double canvasW, double canvasH, double yMin, double yMax, double yRange)
    {
        var gridBrush = (Brush)FindResource("DividerBrush");
        int gridLines = 5;
        for (int i = 0; i <= gridLines; i++)
        {
            double y = canvasH * i / gridLines;
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = canvasW, Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 0.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
            };
            PlotCanvas.Children.Add(line);
        }
    }

    private void DrawYAxisLabels(double canvasH, double yMin, double yMax)
    {
        var textBrush = (Brush)FindResource("InkTertiaryBrush");
        int labelCount = 5;
        double yRange = yMax - yMin;
        for (int i = 0; i <= labelCount; i++)
        {
            double y = canvasH * i / labelCount;
            double value = yMax - (yRange * i / labelCount);
            var tb = new TextBlock
            {
                Text = value.ToString("F1"),
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = textBrush,
            };
            Canvas.SetLeft(tb, 2);
            Canvas.SetTop(tb, y - 7);
            YAxisCanvas.Children.Add(tb);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Clear();
        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        LatestValueText.Text = "--";
        PointCountText.Text = "  (0 points)";
        YRangeText.Text = "--";
        StatusText.Text = "Waiting for data...";
    }
}
