using System.Windows;
using System.Windows.Controls;
namespace GooglyWindows.Automation;
public static class ActionConfirmation
{
    public static async Task<bool> AskAsync(string description, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Window? window = null;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = "Allow this action?", FontSize = 24 });
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, MaxWidth = 430 });
            var yes = new Button { Content = "Allow once" }; var no = new Button { Content = "Cancel" };
            panel.Children.Add(yes); panel.Children.Add(no);
            window = new Window { Title = "Bluey needs your confirmation", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, Background = System.Windows.Media.Brushes.Black };
            yes.Click += (_, _) => { if (!ct.IsCancellationRequested) completion.TrySetResult(true); window.Close(); };
            no.Click += (_, _) => window.Close();
            window.Closed += (_, _) => completion.TrySetResult(false);
            window.Show();
        });
        using var registration = ct.Register(() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { completion.TrySetResult(false); window?.Close(); }));
        return await completion.Task;
    }
}
