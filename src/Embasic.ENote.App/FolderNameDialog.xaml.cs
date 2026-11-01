using System.Windows;

namespace Embasic.ENote.App;

public partial class FolderNameDialog : Window
{
    public string FolderName => FolderNameInput.Text;

    public FolderNameDialog() => InitializeComponent();

    private void Dialog_Loaded(object sender, RoutedEventArgs e) =>
        FolderNameInput.Focus();

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderName)) return;
        DialogResult = true;
    }
}
