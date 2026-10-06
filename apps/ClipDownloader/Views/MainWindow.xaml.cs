using System.Windows;
using ClipDownloader.ViewModels;

namespace ClipDownloader.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
