using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AutoSplit_AutoMask;

public enum MessageBoxButton { Ok, YesNo, YesNoCancel }
public enum MessageBoxResult { Ok, Yes, No, Cancel }

public class MessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    private MessageBox() { }

    public static Task<MessageBoxResult> Show(Window owner, string title, string message,
        MessageBoxButton buttons = MessageBoxButton.Ok)
    {
        var msgBox = new MessageBox
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#2C2C2C")),
            FontSize = 12,
        };

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 20, 20, 16),
            MaxWidth = 380,
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 16),
        };

        void AddButton(string content, MessageBoxResult result, bool isDefault = false)
        {
            var btn = new Button
            {
                Content = content,
                MinWidth = 72,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (isDefault)
            {
                btn.Classes.Add("accent");
            }
            btn.Click += (_, _) =>
            {
                msgBox._result = result;
                msgBox.Close();
            };
            buttonPanel.Children.Add(btn);
        }

        switch (buttons)
        {
            case MessageBoxButton.Ok:
                AddButton("OK", MessageBoxResult.Ok, isDefault: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel);
                break;
        }

        msgBox.Content = new StackPanel
        {
            Children = { messageText, buttonPanel },
        };

        var tcs = new TaskCompletionSource<MessageBoxResult>();
        msgBox.Closed += (_, _) => tcs.TrySetResult(msgBox._result);
        msgBox.ShowDialog(owner);
        return tcs.Task;
    }
}
