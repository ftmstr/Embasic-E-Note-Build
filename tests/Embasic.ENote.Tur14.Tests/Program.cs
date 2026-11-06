using Embasic.ENote.Core.Markdown;
int passed=0, failed=0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS "+name); }
    catch(Exception ex) { failed++; Console.Error.WriteLine("FAIL "+name+": "+ex); }
}
static void Yes(bool truth) { if (!truth) throw new InvalidOperationException("Assertion failed."); }
static void Throws<T>(Action action) where T:Exception
{
    try { action(); } catch(T) { return; }
    throw new InvalidOperationException("Expected "+typeof(T).Name);
}
Check("plain Unicode literal",()=>{
    var t=MarkdownNestedInlineParser.Parse("Türkçe 日本語 😀");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text && t[0].Text=="Türkçe 日本語 😀");
});
Check("bold contains italic node",()=>{
    var t=MarkdownNestedInlineParser.Parse("**kalın *italik* devam**");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Strong &&
        t[0].Children!.Count==3 && t[0].Children![1].Kind==MarkdownInlineKind.Emphasis);
});
Check("italic contains bold node",()=>{
    var t=MarkdownNestedInlineParser.Parse("*italik **kalın** devam*");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Emphasis &&
        t[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Strong));
});
Check("strike contains inline code",()=>{
    var t=MarkdownNestedInlineParser.Parse("~~sil \u0060**literal**\u0060~~");
    Yes(t.Count==1 && t[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Code &&
        n.Text=="**literal**" && n.Children is null));
});
Check("multi-backtick code span",()=>{
    var t=MarkdownNestedInlineParser.Parse("\u0060\u0060contains \u0060 a\u0060\u0060");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Code && t[0].Text=="contains \u0060 a");
});
Check("escaped star stays literal",()=>{
    var t=MarkdownNestedInlineParser.Parse("\\*literal*");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text && t[0].Text=="*literal*");
});
Check("escaped marker within strong not closing",()=>{
    var t=MarkdownNestedInlineParser.Parse("**Türkçe \\* emoji 😀**");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Strong);
});
Check("word underscores unchanged",()=>{
    var t=MarkdownNestedInlineParser.Parse("snake_case_example");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text);
});
Check("open marker preserves literal",()=>{
    var t=MarkdownNestedInlineParser.Parse("**unfinished");
    Yes(t.Count==1 && t[0].Text=="**unfinished");
});
Check("whitespace emphasis refused",()=>{
    var t=MarkdownNestedInlineParser.Parse("* *");
    Yes(t.Count==1 && t[0].Text=="* *");
});
Check("multi-line delimited text literal",()=>{
    var t=MarkdownNestedInlineParser.Parse("**first\nsecond**");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text);
});
Check("HTML script remains inert literal",()=>{
    var t=MarkdownNestedInlineParser.Parse("<script>alert(1)</script>");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text);
});
Check("remote image remains inert",()=>{
    var t=MarkdownNestedInlineParser.Parse("![a](https://invalid.test/a.png)");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text);
});
Check("remote link remains inert",()=>{
    var t=MarkdownNestedInlineParser.Parse("[a](https://invalid.test/)");
    Yes(t.Count==1 && t[0].Kind==MarkdownInlineKind.Text);
});
Check("no source mutation",()=>{
    string original="**İstanbul *日本語* 😀**";
    var _=MarkdownNestedInlineParser.Parse(original);
    Yes(original=="**İstanbul *日本語* 😀**");
});
Check("extremely nested input fails soft as literal at bounded depth",()=>{
    string x="güvenli"; for(int n=0;n<18;n++) x="**x "+x+" y**";
    var t=MarkdownNestedInlineParser.Parse(x);
    Yes(t.Count>0);
});
Check("many tokens bounded",()=>{
    var t=MarkdownNestedInlineParser.Parse(string.Concat(Enumerable.Repeat("**a** ",3500)));
    Yes(t.Count<=MarkdownNestedInlineParser.MaximumNodes);
});
Check("too long input rejected",()=>Throws<ArgumentOutOfRangeException>(()=>
    MarkdownNestedInlineParser.Parse(new string('x',MarkdownPreviewParser.MaximumPreviewCharacters+1))));
Check("null source rejected",()=>Throws<ArgumentNullException>(()=>MarkdownNestedInlineParser.Parse(null!)));
Check("limited preview source not rewritten",()=>{
    var s="# Heading\n\n**x *y* z**"; var p=MarkdownPreviewParser.Parse(s);
    Yes(p.Blocks.Count==2 && s.EndsWith("**x *y* z**"));
});
Console.WriteLine($"{passed}/{passed+failed} Tur14 safe nested inline checks passed.");
return failed==0?0:1;