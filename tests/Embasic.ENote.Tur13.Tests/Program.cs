using Embasic.ENote.Core.Markdown;

int passed=0, failed=0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS "+name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL "+name+": "+error); }
}
static void Yes(bool ok) { if(!ok) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch(T) { return; }
    throw new Exception("Expected "+typeof(T).Name);
}
Check("empty inline text generates no runs",()=>Yes(MarkdownInlineParser.Parse("").Count==0));
Check("plain text remains unstyled",()=>{
    var r=MarkdownInlineParser.Parse("Türkçe 日本語 😀");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text && r[0].Text=="Türkçe 日本語 😀");
});
Check("bold surrounded by literal text",()=>{
    var r=MarkdownInlineParser.Parse("one **kalın** two");
    Yes(r.Count==3 && r[1].Kind==MarkdownInlineKind.Strong && r[1].Text=="kalın");
});
Check("italic asterisk is styled",()=>{
    var r=MarkdownInlineParser.Parse("*italik*");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Emphasis && r[0].Text=="italik");
});
Check("underscore italic is styled",()=>{
    var r=MarkdownInlineParser.Parse("_italik_");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Emphasis);
});
Check("underscore inside word remains literal",()=>{
    var r=MarkdownInlineParser.Parse("snake_case_test");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text && r[0].Text=="snake_case_test");
});
Check("strikethrough explicitly marked",()=>{
    var r=MarkdownInlineParser.Parse("~~üstü çizili~~");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Strikethrough &&
        r[0].Text=="üstü çizili");
});
Check("inline code preserves punctuation literally",()=>{
    var r=MarkdownInlineParser.Parse("\u0060**literal** <script>\u0060");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Code && r[0].Text=="**literal** <script>");
});
Check("escaped emphasis delimiter stays literal",()=>{
    var r=MarkdownInlineParser.Parse("\\*not italic*");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text && r[0].Text=="*not italic*");
});
Check("escaped strong closing marker not prematurely consumed",()=>{
    var r=MarkdownInlineParser.Parse("**x\\*y**");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Strong && r[0].Text=="x*y");
});
Check("unterminated marker is preserved",()=>{
    var r=MarkdownInlineParser.Parse("**unfinished");
    Yes(r.Count==1 && r[0].Text=="**unfinished");
});
Check("whitespace-only emphasis not interpreted",()=>{
    var r=MarkdownInlineParser.Parse("* *");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text && r[0].Text=="* *");
});
Check("literal HTML tag and script do not become markup objects",()=>{
    var r=MarkdownInlineParser.Parse("<img src='https://invalid.test/a'>");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text && r[0].Text.StartsWith("<img"));
});
Check("remote Markdown link stays inert text",()=>{
    var r=MarkdownInlineParser.Parse("[visit](https://invalid.test/path)");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text);
});
Check("image Markdown remains inert literal",()=>{
    var r=MarkdownInlineParser.Parse("![alt](https://invalid.test/p.png)");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text);
});
Check("multi-style sequence and emoji",()=>{
    var r=MarkdownInlineParser.Parse("**İ** + *日* + ~~😀~~");
    Yes(r.Count==5 && r[0].Kind==MarkdownInlineKind.Strong &&
        r[2].Kind==MarkdownInlineKind.Emphasis && r[4].Kind==MarkdownInlineKind.Strikethrough);
});
Check("line break does not cross emphasis",()=>{
    var r=MarkdownInlineParser.Parse("*first\nsecond*");
    Yes(r.Count==1 && r[0].Kind==MarkdownInlineKind.Text);
});
Check("source Markdown string never modified",()=>{
    string source=String.Concat(Enumerable.Repeat("**Kaynak** \u0060x\u0060 ",300));
    var _=MarkdownInlineParser.Parse(source);
    Yes(source.StartsWith("**Kaynak**") && source.EndsWith(" \u0060x\u0060 "));
});
Check("preview parser rejects excessive inline source",()=>
    Throws<ArgumentOutOfRangeException>(()=>MarkdownInlineParser.Parse(
        new string('x',MarkdownPreviewParser.MaximumPreviewCharacters+1))));
Check("null inline text rejected",()=>
    Throws<ArgumentNullException>(()=>MarkdownInlineParser.Parse(null!)));
Console.WriteLine($"{passed}/{passed+failed} Tur13 inert inline Markdown tests passed.");
return failed==0?0:1;