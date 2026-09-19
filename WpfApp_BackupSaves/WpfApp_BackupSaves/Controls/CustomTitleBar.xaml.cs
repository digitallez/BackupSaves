using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using WpfApp_BackupSaves.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace WpfApp_BackupSaves.Controls;

public partial class CustomTitleBar : UserControl
{
    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(CustomTitleBar),
            new PropertyMetadata(true, OnShowMaximizeChanged));

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    private Window? _window;

    public CustomTitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnShowMaximizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomTitleBar bar)
            bar.MaximizeButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null)
            return;

        TitleText.Text = _window.Title;
        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.TitleProperty, typeof(Window));
        dpd?.AddValueChanged(_window, OnWindowTitleChanged);

        if (_window.Icon is BitmapFrame frame)
            IconImage.Source = frame;
        else if (_window.Icon is not null)
            IconImage.Source = _window.Icon;

        _window.StateChanged += OnWindowStateChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateMaximizeGlyph();
        MaximizeButton.Visibility = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
        WindowChrome.SetIsHitTestVisibleInChrome(this, true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        if (_window is null) return;
        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.TitleProperty, typeof(Window));
        dpd?.RemoveValueChanged(_window, OnWindowTitleChanged);
        _window.StateChanged -= OnWindowStateChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();

    private void OnWindowTitleChanged(object? sender, EventArgs e)
    {
        if (_window is not null)
            TitleText.Text = _window.Title;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeGlyph();

    private void UpdateMaximizeGlyph()
    {
        MaximizeButton.Content = _window?.WindowState == WindowState.Maximized ? "❐" : "☐";
        MaximizeButton.ToolTip = _window?.WindowState == WindowState.Maximized
            ? LocalizationService.Text("titlebar.restore")
            : LocalizationService.Text("titlebar.maximize");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_window is null) return;
        if (e.ClickCount == 2 && ShowMaximize)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            _window.DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => _window?.Close();

    private void ToggleMaximize()
    {
        if (_window is null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
