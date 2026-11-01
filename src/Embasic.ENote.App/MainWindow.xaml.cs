using System.Windows;
using Embasic.ENote.App.ViewModels;

namespace Embasic.ENote.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
