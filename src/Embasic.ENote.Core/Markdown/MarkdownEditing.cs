using System.Text;

namespace Embasic.ENote.Core.Markdown;

/// <summary>Literal editor operations; no regex, file access or secret-note paths.</summary>
public static class MarkdownEditing
{
    public static int Find(string text,string query,int start,bool backwards,bool matchCase)
    {
        if(query.Length==0||text.Length==0)return -1;
        var comparison=matchCase?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        start=Math.Clamp(start,0,text.Length);
        if(backwards)
        {
            int found=start>0?text.LastIndexOf(query,start-1,comparison):-1;
            return found>=0?found:text.LastIndexOf(query,comparison);
        }
        int next=text.IndexOf(query,start,comparison);return next>=0?next:text.IndexOf(query,comparison);
    }
    public static (string Text,int Count) ReplaceAll(string text,string query,string replacement,bool matchCase)
    {
        if(query.Length==0)return(text,0);
        var comparison=matchCase?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        var indexes=new List<int>();int start=0,index;
        while((index=text.IndexOf(query,start,comparison))>=0){indexes.Add(index);start=index+query.Length;}
        if(indexes.Count==0)return(text,0);
        long length=text.Length+(long)indexes.Count*(replacement.Length-query.Length);
        if(length>16_000_000)throw new NotSupportedException("Bu toplu değiştirme çok büyük. Daha küçük parçalar halinde değiştirin.");
        var result=new StringBuilder((int)length);start=0;
        foreach(int at in indexes){result.Append(text,start,at-start).Append(replacement);start=at+query.Length;}
        result.Append(text,start,text.Length-start);return(result.ToString(),indexes.Count);
    }
}
