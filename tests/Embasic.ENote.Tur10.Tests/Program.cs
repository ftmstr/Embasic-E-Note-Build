using Embasic.ENote.Core.Markdown;
int ok=0,failed=0;
void Check(string title, Action fn) {
    try { fn(); ok++; Console.WriteLine("PASS "+title); }
    catch(Exception e){failed++; Console.Error.WriteLine("FAIL "+title+": "+e);}
}
static void Yes(bool b){if(!b)throw new Exception("assert");}
Check("empty source produces zero blocks",()=>Yes(MarkdownPreviewParser.Parse("").Blocks.Count==0));
Check("source string unchanged",()=>{
    string s="# Title\n\nHello";var v=MarkdownPreviewParser.Parse(s);
    Yes(v.Blocks.Count==2 && s=="# Title\n\nHello");
});
Check("Unicode heading literal",()=>Yes(MarkdownPreviewParser.Parse("# Türkçe 日本語 😀").Blocks[0].Text=="Türkçe 日本語 😀"));
Check("six heading levels",()=>{
    var r=MarkdownPreviewParser.Parse(string.Join("\n",Enumerable.Range(1,6).Select(n=>new string('#',n)+" Heading")));
    Yes(r.Blocks.Count==6 && r.Blocks[^1].HeadingLevel==6);
});
Check("no space is not heading",()=>Yes(MarkdownPreviewParser.Parse("#nospace").Blocks[0].Kind==MarkdownBlockKind.Paragraph));
Check("group bullets",()=>{
    var v=MarkdownPreviewParser.Parse("- one\n* two\n+ three").Blocks[0];
    Yes(v.Kind==MarkdownBlockKind.BulletList && v.Rows!.SequenceEqual(new[]{"one","two","three"}));
});
Check("group ordered list",()=>Yes(MarkdownPreviewParser.Parse("1. one\n2) two").Blocks[0].Rows!.Count==2));
Check("quote lines",()=>Yes(MarkdownPreviewParser.Parse("> First\n> Second").Blocks[0].Text=="First\nSecond"));
Check("fenced code not interpreted",()=>{
    var r=MarkdownPreviewParser.Parse("\u0060\u0060\u0060cs\n# literal\n- literal\n\u0060\u0060\u0060");
    Yes(r.Blocks.Count==1 && r.Blocks[0].Text.Contains("# literal") && r.Blocks[0].Kind==MarkdownBlockKind.Code);
});
Check("tilde fence",()=>Yes(MarkdownPreviewParser.Parse("~~~\ntest\n~~~").Blocks[0].Kind==MarkdownBlockKind.Code));
Check("unclosed fence safe",()=>Yes(MarkdownPreviewParser.Parse("\u0060\u0060\u0060\nhello").Blocks[0].Kind==MarkdownBlockKind.Code));
Check("GFM-like table basic",()=>{
    var r=MarkdownPreviewParser.Parse("| A | B |\n| --- | :---: |\n| 1 | 2 |");
    Yes(r.Blocks.Count==1 && r.Blocks[0].Kind==MarkdownBlockKind.Table && r.Blocks[0].Rows![1]=="1\t2");
});
Check("invalid table delimiter stays text",()=>Yes(!MarkdownPreviewParser.Parse("|a|b|\n|--|--|").Blocks.Any(x=>x.Kind==MarkdownBlockKind.Table)));
Check("horizontal rule",()=>Yes(MarkdownPreviewParser.Parse("---").Blocks[0].Kind==MarkdownBlockKind.Rule));
Check("CRLF normalized only in preview",()=>Yes(MarkdownPreviewParser.Parse("a\r\nb\rc").Blocks[0].Text=="a\nb\nc"));
Check("HTML script rendered as literal text",()=>{
    var r=MarkdownPreviewParser.Parse("<script>alert(1)</script>");
    Yes(r.Blocks[0].Kind==MarkdownBlockKind.Paragraph && r.Blocks[0].Text.StartsWith("<script>"));
});
Check("images are not fetched",()=>Yes(MarkdownPreviewParser.Parse("![x](https://invalid.test/a.png)").Blocks[0].Kind==MarkdownBlockKind.Paragraph));
Check("links are not navigable",()=>Yes(MarkdownPreviewParser.Parse("[x](https://invalid.test/)").Blocks[0].Text.StartsWith("[x]")));
Check("character guard truncates preview not original",()=>{
    string s=new('x',MarkdownPreviewParser.MaximumPreviewCharacters+10);
    var r=MarkdownPreviewParser.Parse(s);
    Yes(r.IsTruncated && r.Blocks[^1].Kind==MarkdownBlockKind.Notice && s.Length>MarkdownPreviewParser.MaximumPreviewCharacters);
});
Check("line guard truncates preview",()=>{
    var r=MarkdownPreviewParser.Parse(string.Concat(Enumerable.Repeat("line\n",MarkdownPreviewParser.MaximumPreviewLines+1)));
    Yes(r.IsTruncated && r.Blocks[^1].Kind==MarkdownBlockKind.Notice);
});
Check("null source rejected",()=>{
    try {MarkdownPreviewParser.Parse(null!);throw new Exception("not rejected");}
    catch(ArgumentNullException){}
});
Console.WriteLine($"{ok}/{ok+failed} Tur10 offline Markdown preview checks passed.");
return failed==0?0:1;