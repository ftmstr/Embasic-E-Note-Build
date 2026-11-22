using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Embasic.ENote.Core.Markdown;
using Embasic.ENote.Storage.Documents;
using Microsoft.Win32;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private TextBox? ActiveEditor => FindEditor(MarkdownTabs);
    private TextBox? FindEditor(DependencyObject root)
    {
        if(root is TextBox text && ReferenceEquals(text.DataContext,SelectedTab) &&
            AutomationProperties.GetAutomationId(text)=="MarkdownSourceText")return text;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(FindEditor(VisualTreeHelper.GetChild(root,i)) is { } found)return found;
        return null;
    }
    private void ShowFind_Click(object sender,RoutedEventArgs e)
    {
        MainSections.SelectedItem=MarkdownSectionTab;
        if(SelectedTab is null){MarkdownStatusText.Text="Aramak için bir Markdown notu açın.";return;}
        if(SelectedTab.ViewMode==ViewModels.MarkdownEditorViewMode.Preview)MarkdownModeSelector.SelectedIndex=0;
        FindBar.Visibility=Visibility.Visible;SearchTermBox.Focus();SearchTermBox.SelectAll();
    }
    private void HideFind_Click(object sender,RoutedEventArgs e){FindBar.Visibility=Visibility.Collapsed;ActiveEditor?.Focus();}
    private void FindQuery_Changed(object sender,RoutedEventArgs e)
    {if(FindStatusText is not null)FindStatusText.Text="Metin araması · Enter: sonraki · Shift+Enter: önceki";}
    private void FindTerm_KeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Enter){FindInNote(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));e.Handled=true;}}
    private void FindNext_Click(object sender,RoutedEventArgs e)=>FindInNote(false);
    private void FindPrevious_Click(object sender,RoutedEventArgs e)=>FindInNote(true);
    private void FindInNote(bool backwards)
    {
        if(ActiveEditor is not { } editor || SearchTermBox.Text.Length==0)return;
        int start=backwards?editor.SelectionStart:editor.SelectionStart+editor.SelectionLength;
        int index=MarkdownEditing.Find(editor.Text,SearchTermBox.Text,start,backwards,FindCaseSensitive.IsChecked==true);
        if(index<0){FindStatusText.Text="Eşleşme bulunamadı.";return;}
        editor.Select(index,SearchTermBox.Text.Length);editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(index));
        FindStatusText.Text=$"Eşleşme bulundu · {index+1}. karakter";
    }
    private void ReplaceOne_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveEditor is not { } editor || SearchTermBox.Text.Length==0)return;
        var comparison=FindCaseSensitive.IsChecked==true?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        if(!string.Equals(editor.SelectedText,SearchTermBox.Text,comparison)){FindInNote(false);return;}
        editor.BeginChange();try{editor.SelectedText=ReplaceTermBox.Text;}finally{editor.EndChange();}
        FindInNote(false);
    }
    private void ReplaceAll_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveEditor is not { } editor)return;
        try
        {
            var changed=MarkdownEditing.ReplaceAll(editor.Text,SearchTermBox.Text,ReplaceTermBox.Text,FindCaseSensitive.IsChecked==true);
            if(changed.Count>0){editor.BeginChange();try{editor.SelectAll();editor.SelectedText=changed.Text;editor.Select(0,0);}finally{editor.EndChange();}}
            FindStatusText.Text=$"{changed.Count} eşleşme değiştirildi. Geri al: Ctrl+Z.";
            editor.Focus();
        }
        catch(NotSupportedException error){FindStatusText.Text=error.Message;}
    }
    private void FormatMarkdown_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveEditor is not { } editor || sender is not Button button)return;
        string tag=button.Tag?.ToString()??"",prefix="",suffix="";
        switch(tag)
        {
            case "bold":prefix=suffix="**";break;case "italic":prefix=suffix="*";break;case "code":prefix=suffix="`";break;
            case "heading":prefix="# ";break;case "list":prefix="- ";break;case "quote":prefix="> ";break;
            case "table":prefix="\n| Başlık 1 | Başlık 2 |\n| --- | --- |\n| Hücre 1 | Hücre 2 |\n";break;
        }
        int at=editor.SelectionStart,len=editor.SelectionLength;editor.BeginChange();
        try{editor.SelectedText=prefix+editor.SelectedText+suffix;editor.Select(at+prefix.Length,len);}finally{editor.EndChange();}
        editor.Focus();
    }
    private void ExportCopy_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedTab is not { } tab)return;
        if(MessageBox.Show(this,"Açık notun kaydedilmemiş değişiklikleri dahil yeni bir şifresiz kopyası oluşturulur. Kaynak dosya değişmez. Başlık, liste, tablo ve temel metin biçimleri aktarılır; görsel ve bağlantılar metin olarak kalır. Karmaşık belge sadakati garanti edilmez.","Belge Kopyası",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        var dialog=new SaveFileDialog{Title="Yeni belge kopyası oluştur",Filter="Word (.docx)|*.docx|Rich Text (.rtf)|*.rtf|HTML (.html)|*.html|Düz metin (.txt)|*.txt|Markdown (.md)|*.md|OpenDocument (.odt)|*.odt",DefaultExt=".docx",AddExtension=true,OverwritePrompt=false,CheckPathExists=true,InitialDirectory=Path.GetDirectoryName(tab.FilePath),FileName=Path.GetFileNameWithoutExtension(tab.FilePath)+"-kopya"};
        if(dialog.ShowDialog(this)!=true)return;
        try{new MarkdownCopyExportStore().WriteNew(tab.FilePath,tab.Text,dialog.FileName,tab.Layout);MarkdownStatusText.Text="Yeni belge kopyası oluşturuldu: "+Path.GetFileName(dialog.FileName);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Xml.XmlException)
        {MessageBox.Show(this,"Kopya oluşturulamadı: "+error.Message,"Embasic E-Note",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
}
