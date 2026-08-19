using System.Windows;
using FalconCanBridge.App.ViewModels;

namespace FalconCanBridge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
