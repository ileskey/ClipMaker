using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipDownloader.ViewModels;

namespace ClipDownloader.Views;

public partial class MainWindow : Window
{
    private bool allowClose;
    private bool closePending;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true;
            if (closePending) return;
            closePending = true;
            ShutdownStarted?.Invoke(this, EventArgs.Empty);
            await viewModel.ShutdownAsync();
            if (viewModel.RequiresCloseAcknowledgement) return;
            viewModel.MarkClosed();
            allowClose = true;
            // Even an already-completed shutdown must leave Closing first.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        };
        viewModel.FocusErrorRequested += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (string.IsNullOrEmpty(viewModel.ValidationSummary)) { StatusLabel.BringIntoView(); StatusLabel.Focus(); }
            else { ErrorSummary.BringIntoView(); ErrorSummary.Focus(); }
        }));
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ShowingConfirmation) && viewModel.ShowingConfirmation)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { ConfirmationHeading.BringIntoView(); ConfirmationHeading.Focus(); }));
        };
    }

    public event EventHandler? ShutdownStarted;
    private void AcknowledgeClose(object sender, RoutedEventArgs args)
    {
        var viewModel = (MainViewModel)DataContext;
        if (!viewModel.CanAcknowledgeClose) return;
        viewModel.MarkClosed();
        allowClose = true;
        Close();
    }

    private void TimeFieldLostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is not TextBox box || box.Tag is not string field) return;
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding?.ResolvedSource is TimecodeInputViewModel time) time.Normalize(field);
    }
}
