using Embasic.ENote.Emd;
using System.Security.Cryptography;
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
Check("terminal even backslashes keep proper table frame",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B\\\\|\n| --- | --- |\n| 1 | 2 |");
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Table
        && x.Blocks[0].TableCells![0].Count==2);
});
Check("terminal odd escaped pipe is literal data",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B\\|\n| --- | --- |\n| 1 | 2 |");
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Table
        && x.Blocks[0].TableCells![0][1]=="B|");
});
Check("unclosed code span in table row falls back verbatim",()=>{
    string source="| A | B |\n| --- | --- |\n| \u0060not closed | x |";
    var x=MarkdownPreviewParser.Parse(source);
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Paragraph
        && x.Blocks[0].Text==source);
});
Check("unclosed code span in header remains text",()=>{
    var x=MarkdownPreviewParser.Parse("| \u0060unfinished | B |\n| --- | --- |\n| 1 | 2 |");
    Yes(x.Blocks.All(n=>n.Kind!=MarkdownBlockKind.Table));
});
Check("escaped backtick does not swallow column",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B |\n| --- | --- |\n| \\\u0060literal | value |");
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Table
        && x.Blocks[0].TableCells![1].Count==2);
});
Check("triple backtick span contains pipe",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B |\n| --- | --- |\n| \u0060\u0060\u0060a|b\u0060\u0060\u0060 | ikinci |");
    Yes(x.Blocks[0].Kind==MarkdownBlockKind.Table
        && x.Blocks[0].TableCells![1][0].Contains("a|b"));
});
Check("mismatched code span length literal fallback",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B |\n| --- | --- |\n| \u0060\u0060a|b\u0060 | x |");
    Yes(x.Blocks[0].Kind==MarkdownBlockKind.Paragraph
        && x.Blocks[0].Text.Contains("a|b"));
});
Check("table columns above preview cap never silently lost",()=>{
    int n=MarkdownPreviewParser.MaximumPreviewTableColumns+1;
    string header="| "+string.Join(" | ",Enumerable.Repeat("A",n))+" |";
    string delimit="| "+string.Join(" | ",Enumerable.Repeat("---",n))+" |";
    string source=header+"\n"+delimit+"\n"+header;
    var x=MarkdownPreviewParser.Parse(source);
    Yes(x.Blocks.All(b=>b.Kind!=MarkdownBlockKind.Table)
        && source.Contains(delimit));
});
Check("table rows above preview cap revert to literal source",()=>{
    const string header="| A | B |\n| --- | --- |\n";
    string data=string.Concat(Enumerable.Repeat("| T | V |\n",
        MarkdownPreviewParser.MaximumPreviewTableRows+3));
    string source=header+data;
    var x=MarkdownPreviewParser.Parse(source);
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Paragraph
        && x.Blocks[0].Text.Contains("| T | V |"));
});
Check("extra column never silently discarded",()=>{
    string source="| A | B |\n| --- | --- |\n| 1 | 2 | EXTRA |";
    var x=MarkdownPreviewParser.Parse(source);
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Paragraph
        && x.Blocks[0].Text==source);
});
Check("literal tab in Unicode table remains in cell",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B |\n| --- | --- |\n| Çığ\t日本語 | 😀 |");
    Yes(x.Blocks[0].TableCells![1][0]=="Çığ\t日本語");
});
Check("malicious HTML in table cannot create active content",()=>{
    var x=MarkdownPreviewParser.Parse("| A | B |\n| --- | --- |\n| <script>x</script> | [x](https://bad.invalid/) |");
    Yes(x.Blocks[0].TableCells![1][0]=="<script>x</script>"
        && x.Blocks[0].TableCells![1][1].StartsWith("[x]"));
});
Check("mixed newlines normalized only for preview",()=>{
    string original="| A | B |\r\n| --- | --- |\r\n| Türkçe | 日本語 |";
    var x=MarkdownPreviewParser.Parse(original);
    Yes(x.Blocks.Count==1 && x.Blocks[0].Kind==MarkdownBlockKind.Table
        && original.Contains("\r\n"));
});
Check("source markdown table text immutable after parsing",()=>{
    string original="| \u0060a|b\u0060 | B\\|C |\n| --- | --- |\n| 🇹🇷 | 😄 |";
    var _=MarkdownPreviewParser.Parse(original);
    Yes(original.Contains("B\\|C") && original.Contains("🇹🇷"));
});
// Experimental crypto policy regression probes. These do not simulate
// another Windows account, a malicious local process or rollback protection.
static void RejectAuth(Action action)
{
    try { action(); }
    catch (CryptographicException) { return; }
    catch (InvalidDataException) { return; }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Unauthorized operation unexpectedly succeeded.");
}
const string oldPassword="Tur15 synthetic password 2026!";
const string recoveryPassword="Tur15 synthetic recovery 2026!";
const string newPassword="Tur15 next password 2027!";
using var rootFactor=LayerSecret.Generate(LayerKind.Folder);
using var childFactor=LayerSecret.Generate(LayerKind.Subfolder);
using var noteFactor=LayerSecret.Generate(LayerKind.NoteAdditional);
using var plaintext=PortableEmd.Encrypt("Tur15 korunan not 🔒",oldPassword);
byte[] outer=LayeredEmd3.Seal(plaintext,[rootFactor,childFactor,noteFactor]);
Guid workspaceId=Guid.NewGuid();
var members=new[]
{
    new WorkspaceLockEntry(rootFactor.Id,Guid.Empty,LayerKind.Folder,"Klasör"),
    new WorkspaceLockEntry(childFactor.Id,rootFactor.Id,LayerKind.Subfolder,"Klasör/Alt"),
    new WorkspaceLockEntry(noteFactor.Id,childFactor.Id,LayerKind.NoteAdditional,"Klasör/Alt/Gizli.emd")
};
byte[] hierarchy=WorkspaceLockIndex.Protect(workspaceId,rootFactor,members);
Check("ELK1 full independent factor chain decrypts synthetic note",()=>{
    Yes(LayeredEmd3.Open(outer,[rootFactor,childFactor,noteFactor],password:oldPassword)
        =="Tur15 korunan not 🔒");
});
Check("ELK1 missing subfolder factor is rejected",()=>
    RejectAuth(()=>LayeredEmd3.Open(outer,[rootFactor,noteFactor],password:oldPassword)));
Check("ELK1 missing additional note factor is rejected",()=>
    RejectAuth(()=>LayeredEmd3.Open(outer,[rootFactor,childFactor],password:oldPassword)));
Check("ELK1 tampered ciphertext refuses decryption",()=>{
    var changed=outer.ToArray(); changed[^1]^=0x20;
    RejectAuth(()=>LayeredEmd3.Open(changed,[rootFactor,childFactor,noteFactor],password:oldPassword));
});
Check("ELH1 wrong workspace identifier refuses root authentication",()=>
    RejectAuth(()=>WorkspaceLockIndex.Open(hierarchy,Guid.NewGuid(),rootFactor)));
Check("ELH1 modified authenticated hierarchy refuses open",()=>{
    var changed=hierarchy.ToArray(); changed[^1]^=0x10;
    RejectAuth(()=>WorkspaceLockIndex.Open(changed,workspaceId,rootFactor));
});
Check("ELH1 replacing root factor cannot open encrypted ancestry",()=>{
    using var different=LayerSecret.Generate(LayerKind.Folder);
    RejectAuth(()=>WorkspaceLockIndex.Open(hierarchy,workspaceId,different));
});
byte[] originalVault=LayerKeyVault.Create(rootFactor,oldPassword,recoveryPassword);
byte[] rotatedVault=LayerKeyVault.Rotate(originalVault,rootFactor.Id,rootFactor.Kind,
    oldPassword,newPassword,recoveryPassword);
Check("rotated current ELV1 vault rejects previous credential",()=>
    RejectAuth(()=>LayerKeyVault.Open(rotatedVault,rootFactor.Id,rootFactor.Kind,oldPassword)));
Check("KNOWN ROLLBACK RISK old ELV1 snapshot remains decryptable",()=>{
    // A pass here DOCUMENTS unresolved offline anti-rollback weakness,
    // NOT proof that rotation invalidates old backup copies.
    using var stale=LayerKeyVault.Open(originalVault,rootFactor.Id,rootFactor.Kind,oldPassword);
    Yes(LayeredEmd3.Open(outer,[stale,childFactor,noteFactor],password:oldPassword)
        =="Tur15 korunan not 🔒");
});

Console.WriteLine($"{passed}/{passed+failed} Tur15 triple-markdown safety checks passed.");
return failed==0?0:1;