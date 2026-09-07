using Avalonia.Controls;
using Avalonia.Interactivity;
using MyVPN.Client;

namespace MyVPN.Client.App;

public partial class MainWindow : Window
{
    private readonly DesktopVpnController _controller = new();

    public MainWindow()
    {
        InitializeComponent();
        ApiBox.Text = _controller.Api;
        HintBlock.Text = _controller.KillSwitchHint;
        StatusBlock.Text = _controller.Status;
        _controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DesktopVpnController.Status) or null)
            {
                StatusBlock.Text = _controller.Status;
            }

            if (e.PropertyName is nameof(DesktopVpnController.Busy) or nameof(DesktopVpnController.CanConnect) or nameof(DesktopVpnController.CanStop) or null)
            {
                ConnectButton.IsEnabled = _controller.CanConnect;
                StopButton.IsEnabled = _controller.CanStop;
            }
        };
        _controller.RefreshFromSession();
        ConnectButton.IsEnabled = _controller.CanConnect;
        StopButton.IsEnabled = _controller.CanStop;
        StatusBlock.Text = _controller.Status;
    }

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        _controller.Api = ApiBox.Text ?? string.Empty;
        _controller.Email = EmailBox.Text ?? string.Empty;
        _controller.Password = PasswordBox.Text ?? string.Empty;
        await _controller.ConnectAsync();
    }

    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        _controller.Api = ApiBox.Text ?? string.Empty;
        _controller.Email = EmailBox.Text ?? string.Empty;
        _controller.Password = PasswordBox.Text ?? string.Empty;
        await _controller.StopAsync();
    }
}
