using System.Windows;
using BackupSaves.Core;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

/// <summary>
/// Modal progress dialog with pause/cancel. Optionally stays open after completion
/// to show a summary (restore); otherwise closes when the work finishes (delete).
/// </summary>
public partial class OperationProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly PauseGate _pause = new();
    private readonly bool _closeWhenCompleted;
    private bool _finished;
    private bool _closingFromCode;

    public CancellationToken CancellationToken => _cts.Token;
    public PauseGate PauseGate => _pause;
    public bool WasCancelled { get; private set; }

    public OperationProgressWindow(string title, string initialStatus, bool closeWhenCompleted)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        Title = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.Text("common.appName")
            : title;
        StatusBlock.Text = initialStatus;
        _closeWhenCompleted = closeWhenCompleted;
        Closing += OnClosing;
    }

    /// <summary>Show as modal dialog and run <paramref name="work"/> after the window is rendered.</summary>
    public bool? ShowDialogAndRun(Func<OperationProgressWindow, CancellationToken, Task> work)
    {
        async void OnRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnRendered;
            await RunAsync(work);
        }

        ContentRendered += OnRendered;
        return ShowDialog();
    }

    public void Report(int done, int total, string? status = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(done, total, status));
            return;
        }

        if (_finished)
            return;

        var pct = total <= 0 ? 0 : Math.Clamp(done * 100.0 / total, 0, 100);
        Bar.Value = pct;
        if (!string.IsNullOrWhiteSpace(status))
            StatusBlock.Text = status;
    }

    /// <summary>Mark success/failure, hide pause/cancel, show OK. Does not close the window.</summary>
    public void ShowCompleted(string message, bool success = true)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowCompleted(message, success));
            return;
        }

        _finished = true;
        _pause.Resume();
        Bar.Value = success ? 100 : Bar.Value;
        StatusBlock.Text = message;
        RunningButtons.Visibility = Visibility.Collapsed;
        CompletedButtons.Visibility = Visibility.Visible;
    }

    /// <summary>Close immediately (delete finished / cancelled).</summary>
    public void CloseFromCode()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CloseFromCode);
            return;
        }

        _finished = true;
        _closingFromCode = true;
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // Not shown as dialog yet / already closing.
        }

        Close();
    }

    private async Task RunAsync(Func<OperationProgressWindow, CancellationToken, Task> work)
    {
        try
        {
            await work(this, _cts.Token).ConfigureAwait(true);
            if (_closeWhenCompleted && !_finished)
                CloseFromCode();
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
            if (!_finished)
                CloseFromCode();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Operation progress failed", ex);
            if (_closeWhenCompleted)
            {
                if (!_finished)
                    CloseFromCode();
                return;
            }

            ShowCompleted(ex.Message, success: false);
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_pause.IsPaused)
        {
            _pause.Resume();
            PauseButton.Content = LocalizationService.Text("common.pause");
        }
        else
        {
            _pause.Pause();
            PauseButton.Content = LocalizationService.Text("common.resume");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
            return;
        WasCancelled = true;
        _cts.Cancel();
        _pause.Resume(); // unblock WaitIfPaused so cancel can surface
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _closingFromCode = true;
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
        }

        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_finished || _closingFromCode)
            return;

        // Title bar X while running — cancel and let RunAsync close the window.
        WasCancelled = true;
        _cts.Cancel();
        _pause.Resume();
        e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _pause.Resume();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
