using Embasic.ENote.Core.Markdown;

int passed=0,failed=0;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS "+name); }
    catch(Exception ex) { failed++; Console.Error.WriteLine("FAIL "+name+": "+ex); }
}
static void Yes(bool truth)
{
    if (!truth) throw new InvalidOperationException("Assertion failed.");
}
static void Throws<T>(Action a) where T:Exception
{
    try { a(); } catch(T) { return; }
    throw new InvalidOperationException("Expected "+typeof(T).Name);
}
Check("triple asterisk strong-emphasis",()=>{
    var x=MarkdownNestedInlineParser.Parse("***Birleşik***");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.StrongEmphasis
        && x[0].Children![0].Text=="Birleşik");
});
Check("triple underscore strong-emphasis",()=>{
    var x=MarkdownNestedInlineParser.Parse("___Birleşik___");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.StrongEmphasis);
});
Check("triple contains inner italic",()=>{
    var x=MarkdownNestedInlineParser.Parse("***Türkçe *italik* 😀***");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.StrongEmphasis
        && x[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Emphasis));
});
Check("triple contains inert code",()=>{
    var x=MarkdownNestedInlineParser.Parse("***İstanbul \u0060<script>\u0060***");
    Yes(x.Count==1 && x[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Code
        && n.Text=="<script>"));
});
Check("unterminated triple is literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("***missing**");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text
        && x[0].Text=="***missing**");
});
Check("four-star ambiguous opener is literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("****ambiguous****");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text
        && x[0].Text=="****ambiguous****");
});
Check("four-underscore ambiguous opener is literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("____ambiguous____");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text
        && x[0].Text=="____ambiguous____");
});
Check("triple with newline is literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("***broken\nnewline***");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text);
});
Check("triple intraword underscore is literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("snake___case___x");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text);
});
Check("nested previous bold-italic remains functional",()=>{
    var x=MarkdownNestedInlineParser.Parse("**bold *italik* end**");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Strong
        && x[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Emphasis));
});
Check("nested previous italic-bold remains functional",()=>{
    var x=MarkdownNestedInlineParser.Parse("*italic **kalın** end*");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Emphasis
        && x[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.Strong));
});
Check("nested strike with triple remains inert",()=>{
    var x=MarkdownNestedInlineParser.Parse("~~sil ***Türkçe***~~");
    Yes(x[0].Kind==MarkdownInlineKind.Strikethrough
        && x[0].Children!.Any(n=>n.Kind==MarkdownInlineKind.StrongEmphasis));
});
Check("triple Unicode including surrogate pairs intact",()=>{
    var x=MarkdownNestedInlineParser.Parse("***Çığ 🇹🇷 日本語 😀***");
    Yes(x[0].Children![0].Text=="Çığ 🇹🇷 日本語 😀");
});
Check("raw HTML remains literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("<script>alert(1)</script>");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text);
});
Check("images never yield active nodes",()=>{
    var x=MarkdownNestedInlineParser.Parse("![x](https://invalid.example/i.png)");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text);
});
Check("remote links stay literal",()=>{
    var x=MarkdownNestedInlineParser.Parse("[x](https://invalid.example)");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Text);
});
Check("backtick code disables strong parsing",()=>{
    var x=MarkdownNestedInlineParser.Parse("\u0060***literal***\u0060");
    Yes(x.Count==1 && x[0].Kind==MarkdownInlineKind.Code && x[0].Text=="***literal***");
});
Check("empty source produces empty nodes",()=>
    Yes(MarkdownNestedInlineParser.Parse("").Count==0));
Check("null input rejected",()=>Throws<ArgumentNullException>(()=>
    MarkdownNestedInlineParser.Parse(null!)));
Check("oversized preview input rejected",()=>Throws<ArgumentOutOfRangeException>(()=>
    MarkdownNestedInlineParser.Parse(new string('a',MarkdownPreviewParser.MaximumPreviewCharacters+1))));
Check("triple source immutable after preview",()=>{
    string source="***Kaynak *metin* 😀***";
    _=MarkdownNestedInlineParser.Parse(source);
    Yes(source=="***Kaynak *metin* 😀***");
});
Check("bounded input may render enormous literal without edit",()=>{
    string source=new('a',MarkdownPreviewParser.MaximumPreviewCharacters);
    var x=MarkdownNestedInlineParser.Parse(source);
    Yes(x.Count==1 && x[0].Text.Length==source.Length);
});
Console.WriteLine($"{passed}/{passed+failed} Tur15 triple-markdown safety checks passed.");
return failed==0?0:1;