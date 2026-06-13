using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom;

public partial class ReplayWindow : Window
{
    private readonly SessionRecorder _recorder;
    private readonly Action<LogEntry> _onEntry;
    private readonly string _filePath;
    private readonly bool _isJsonl;
    private CancellationTokenSource? _cts;
    private bool _isPlaying;
    private double _speedMultiplier = 1.0;

    public bool ReplayCompleted { get; private set; }

    public ReplayWindow(string filePath, SessionRecorder recorder, Action<LogEntry> onEntry)
    {
        _filePath = filePath;
        _recorder = recorder;
        _onEntry = onEntry;
        _isJsonl = string.Equals(Path.GetExtension(filePath), ".jsonl", StringComparison.OrdinalIgnoreCase);
        InitializeComponent();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            // Pause
            _recorder.IsPaused = true;
            _isPlaying = false;
            PlayPauseButton.Content = "Play";
            StatusText.Text = "Paused";
        }
        else
        {
            // Play or resume
            if (_cts == null)
                StartReplay();
            else
            {
                _recorder.IsPaused = false;
                _isPlaying = true;
                PlayPauseButton.Content = "Pause";
                StatusText.Text = "Resumed";
            }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        CancelReplay();
        StatusText.Text = "Stopped";
        PlayPauseButton.Content = "Play";
    }

    private void Speed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _speedMultiplier = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void StartReplay()
    {
        _cts = new CancellationTokenSource();
        _isPlaying = true;
        PlayPauseButton.Content = "Pause";
        _recorder.IsPaused = false;
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (_isJsonl)
                {
                    await _recorder.ReplaySessionAsync(
                        _filePath,
                        onEntry: entry => _ = Dispatcher.BeginInvoke(() => _onEntry(entry)),
                        onProgress: (cur, total) => _ = Dispatcher.BeginInvoke(() => UpdateProgress(cur, total)),
                        speedMultiplier: _speedMultiplier,
                        ct: token);
                }
                else
                {
                    await ReplayTextFileAsync(token);
                }

                _ = Dispatcher.BeginInvoke(() =>
                {
                    ReplayCompleted = true;
                    StatusText.Text = "Replay complete";
                    _isPlaying = false;
                    PlayPauseButton.Content = "Play";
                    _cts?.Dispose();
                    _cts = null;
                });
            }
            catch (OperationCanceledException)
            {
                // Expected on stop/cancel
            }
            catch (Exception ex)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = $"Error: {ex.Message}";
                    _isPlaying = false;
                    PlayPauseButton.Content = "Play";
                    _cts?.Dispose();
                    _cts = null;
                });
            }
        }, token);
    }

    /// <summary>
    /// Replays a text-format log file with timing, mirroring the JSONL path.
    /// </summary>
    private async Task ReplayTextFileAsync(CancellationToken ct)
    {
        var exportService = new FileExportService();
        var (rxEntries, txEntries, parsed, _) = exportService.ReplayFromFile(_filePath, 1);

        // Interleave RX and TX by timestamp
        var allEntries = new List<LogEntry>();
        allEntries.AddRange(rxEntries);
        allEntries.AddRange(txEntries);
        allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        int total = allEntries.Count;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            _ = Dispatcher.BeginInvoke(() => _onEntry(allEntries[i]));
            UpdateProgressOnUI(i + 1, total);

            if (_speedMultiplier > 0 && i < total - 1)
            {
                var delay = allEntries[i + 1].Timestamp - allEntries[i].Timestamp;
                if (delay > TimeSpan.Zero)
                {
                    var adjusted = TimeSpan.FromTicks((long)(delay.Ticks / _speedMultiplier));
                    if (adjusted > TimeSpan.FromSeconds(5))
                        adjusted = TimeSpan.FromSeconds(5);
                    await Task.Delay(adjusted, ct).ConfigureAwait(false);
                }
            }

            while (_recorder.IsPaused && !ct.IsCancellationRequested)
                await Task.Delay(50, ct).ConfigureAwait(false);
        }
    }

    private void UpdateProgress(int current, int total)
    {
        ProgressBar.Maximum = total;
        ProgressBar.Value = current;
        ProgressText.Text = $"{current}/{total}";
        StatusText.Text = $"Replaying entry {current}/{total}...";
    }

    private void UpdateProgressOnUI(int current, int total)
    {
        _ = Dispatcher.BeginInvoke(() => UpdateProgress(current, total));
    }

    private void CancelReplay()
    {
        _recorder.IsPaused = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isPlaying = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CancelReplay();
    }
}
