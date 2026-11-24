using System.Windows;
using System.Windows.Automation;

namespace Embasic.ENote.App;

public partial class FolderNameDialog : Window
{
    public string FolderName => FolderNameInput.Text;

    public FolderNameDialog() => InitializeComponent();

    public FolderNameDialog(string title, string label, string initial, string accept) : this()
    {
        Title = title; EntryNameLabel.Text = label;
        AutomationProperties.SetName(FolderNameInput, label);
        FolderNameInput.Text = initial; EntryNameAccept.Content = accept;
    }

    private void Dialog_Loaded(object sender, RoutedEventArgs e)
    { FolderNameInput.Focus(); FolderNameInput.SelectAll(); }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderName)) return;
        DialogResult = true;
    }
}
