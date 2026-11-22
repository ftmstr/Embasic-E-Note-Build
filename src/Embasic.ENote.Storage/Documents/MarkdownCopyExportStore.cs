using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Embasic.ENote.Core.Markdown;
using Embasic.ENote.Core.Printing;

namespace Embasic.ENote.Storage.Documents;

/// <summary>Offline, new-file-only export of the current plain Markdown buffer.
/// Images/links remain inert source text; this API never opens or exports .emd.</summary>
public sealed class MarkdownCopyExportStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace O = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace T = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace S = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace F = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace A = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace M = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public void WriteNew(string sourceMarkdownPath, string text, string destination, PageLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(text); layout.Validate();
        string source = PlainMarkdownDocumentStore.ValidatePath(sourceMarkdownPath);
        string target = Path.GetFullPath(destination);
        string ext = Path.GetExtension(target).ToLowerInvariant();
        if (ext is not (".md" or ".txt" or ".html" or ".rtf" or ".docx" or ".odt"))
            throw new NotSupportedException("Desteklenen kopya biçimleri: MD, TXT, HTML, RTF, DOCX, ODT.");
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Kaynak notun üzerine dışa aktarılamaz.");
        string parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Bağlantılı hedef klasörü reddedildi.");
        if (File.Exists(target) || Directory.Exists(target) || (ext == ".md" && File.Exists(target + ".enote-layout.json")))
            throw new IOException("Hedef veya eski sayfa yan dosyası zaten var. Yeni bir ad seçin.");
        var model = ext == ".md" ? null : MarkdownPreviewParser.Parse(text);
        if (model?.IsTruncated == true)
            throw new NotSupportedException("Belge yapılandırılmış dışa aktarma sınırını aşıyor. Tam metni MD kopyası olarak kaydedin.");
        if (ext != ".md") XmlConvert.VerifyXmlChars(text);
        string temp = Path.Combine(parent, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".enote-tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (ext is ".docx" or ".odt") WritePackage(stream, model!, layout, ext == ".docx");
                else
                {
                    string content = ext switch { ".md" => text, ".txt" => Plain(model!), ".html" => Html(model!, layout), _ => Rtf(model!, layout) };
                    stream.Write(Utf8.GetBytes(content));
                }
                stream.Flush(flushToDisk: true);
            }
            // Atomic new name publication. A colliding destination is never replaced.
            File.Move(temp, target, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static IEnumerable<MarkdownInlineNode> Nodes(string s) => MarkdownNestedInlineParser.Parse(s);
    private static string Literal(MarkdownInlineNode n) => n.Children is null ? n.Text : string.Concat(n.Children.Select(Literal));
    private static string Literal(string s) => string.Concat(Nodes(s).Select(Literal));
    private static IEnumerable<IReadOnlyList<string>> Cells(MarkdownPreviewBlock b) => b.TableCells ??
        (b.Rows ?? []).Select(x => (IReadOnlyList<string>)x.Split('\t')).ToArray();
    private static string Plain(MarkdownPreviewResult model) => string.Join("\n\n", model.Blocks.Select(b => b.Kind switch
    {
        MarkdownBlockKind.Code => b.Text,
        MarkdownBlockKind.Table => string.Join("\n", Cells(b).Select(r => string.Join("\t", r.Select(Literal)))),
        MarkdownBlockKind.BulletList => string.Join("\n", (b.Rows ?? []).Select(x => "• " + Literal(x))),
        MarkdownBlockKind.NumberedList => string.Join("\n", (b.Rows ?? []).Select((x,i) => $"{i+1}. {Literal(x)}")),
        MarkdownBlockKind.Rule => "────────", _ => Literal(b.Text)
    }));
    private static string H(string s) => WebUtility.HtmlEncode(s);
    private static string HtmlInline(MarkdownInlineNode n)
    {
        string value = n.Children is null ? H(n.Text) : string.Concat(n.Children.Select(HtmlInline));
        return n.Kind switch { MarkdownInlineKind.Strong => "<strong>"+value+"</strong>", MarkdownInlineKind.Emphasis => "<em>"+value+"</em>",
            MarkdownInlineKind.StrongEmphasis => "<strong><em>"+value+"</em></strong>", MarkdownInlineKind.Strikethrough => "<s>"+value+"</s>",
            MarkdownInlineKind.Code => "<code>"+value+"</code>", _ => value };
    }
    private static string HtmlText(string s) => string.Concat(Nodes(s).Select(HtmlInline)).Replace("\n", "<br>\n");
    private static string N(decimal n) => n.ToString(CultureInfo.InvariantCulture);
    private static string Html(MarkdownPreviewResult model, PageLayoutSettings p)
    {
        var b = new StringBuilder("<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>Embasic E-Note</title><style>");
        b.Append($"@page{{size:{N(p.WidthMm)}mm {N(p.HeightMm)}mm;margin:{N(p.TopMarginMm)}mm {N(p.RightMarginMm)}mm {N(p.BottomMarginMm)}mm {N(p.LeftMarginMm)}mm}}");
        b.Append("body{font:11pt/1.5 'Segoe UI',sans-serif;max-width:54rem;margin:auto;padding:1rem;color:#172033}table{border-collapse:collapse;width:100%}td,th{border:1px solid #888;padding:.4rem}pre{white-space:pre-wrap}blockquote{border-left:3px solid #888;padding-left:1rem}</style></head><body>");
        foreach (var block in model.Blocks)
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading: b.Append($"<h{block.HeadingLevel}>{HtmlText(block.Text)}</h{block.HeadingLevel}>"); break;
                case MarkdownBlockKind.Code: b.Append("<pre><code>"+H(block.Text)+"</code></pre>"); break;
                case MarkdownBlockKind.Quote: b.Append("<blockquote>"+HtmlText(block.Text)+"</blockquote>"); break;
                case MarkdownBlockKind.Rule: b.Append("<hr>"); break;
                case MarkdownBlockKind.Table:
                    b.Append("<table>"); int row = 0;
                    foreach (var cells in Cells(block)) { string tag = row++ == 0 ? "th" : "td"; b.Append("<tr>"); foreach (var c in cells) b.Append($"<{tag}>{HtmlText(c)}</{tag}>"); b.Append("</tr>"); }
                    b.Append("</table>"); break;
                case MarkdownBlockKind.BulletList: case MarkdownBlockKind.NumberedList:
                    string list = block.Kind == MarkdownBlockKind.BulletList ? "ul" : "ol"; b.Append("<"+list+">");
                    foreach (var item in block.Rows ?? []) b.Append("<li>"+HtmlText(item)+"</li>"); b.Append("</"+list+">"); break;
                default: b.Append("<p>"+HtmlText(block.Text)+"</p>"); break;
            }
        }
        return b.Append("</body></html>").ToString();
    }
    private static int Twips(decimal mm) => (int)decimal.Round(mm * 1440m / 25.4m);
    private static string EscapeRtf(string text)
    {
        var b = new StringBuilder();
        foreach (char c in text)
            if (c is '\\' or '{' or '}') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\line "); else if (c == '\r') { } else if (c == '\t') b.Append("\\tab ");
            else if (c < 128) b.Append(c); else b.Append("\\u").Append(unchecked((short)c)).Append('?');
        return b.ToString();
    }
    private static string RtfNode(MarkdownInlineNode n)
    {
        string text = n.Children is null ? EscapeRtf(n.Text) : string.Concat(n.Children.Select(RtfNode));
        string control = n.Kind switch { MarkdownInlineKind.Strong => "\\b ", MarkdownInlineKind.Emphasis => "\\i ", MarkdownInlineKind.StrongEmphasis => "\\b\\i ", MarkdownInlineKind.Strikethrough => "\\strike ", MarkdownInlineKind.Code => "\\f1 ", _ => "" };
        return "{"+control+text+"}";
    }
    private static string RtfText(string s) => string.Concat(Nodes(s).Select(RtfNode));
    private static string Rtf(MarkdownPreviewResult model, PageLayoutSettings p)
    {
        var b = new StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\uc1{\\fonttbl{\\f0 Segoe UI;}{\\f1 Consolas;}}\\f0\\fs22");
        b.Append($"\\paperw{Twips(p.WidthMm)}\\paperh{Twips(p.HeightMm)}\\margl{Twips(p.LeftMarginMm)}\\margr{Twips(p.RightMarginMm)}\\margt{Twips(p.TopMarginMm)}\\margb{Twips(p.BottomMarginMm)} ");
        foreach (var block in model.Blocks)
        {
            if (block.Kind == MarkdownBlockKind.Table)
            {
                foreach (var row in Cells(block))
                {
                    b.Append("\\trowd "); int width = Twips(p.WidthMm-p.LeftMarginMm-p.RightMarginMm)/row.Count;
                    for(int c=1;c<=row.Count;c++) b.Append("\\cellx").Append(c*width).Append(' ');
                    foreach(var cell in row) b.Append("\\pard\\intbl ").Append(RtfText(cell)).Append("\\cell ");
                    b.Append("\\row ");
                }
            }
            else if (block.Kind is MarkdownBlockKind.BulletList or MarkdownBlockKind.NumberedList)
            { int i=0; foreach(var item in block.Rows ?? []) b.Append("\\pard ").Append(EscapeRtf(block.Kind==MarkdownBlockKind.BulletList ? "• " : $"{++i}. ")).Append(RtfText(item)).Append("\\par "); }
            else
            {
                b.Append("\\pard ");
                b.Append(block.Kind switch { MarkdownBlockKind.Heading => "\\b\\fs"+(40-block.HeadingLevel*2)+" ", MarkdownBlockKind.Code => "\\f1 ", MarkdownBlockKind.Quote => "\\i ", _ => "" });
                b.Append(block.Kind==MarkdownBlockKind.Code ? EscapeRtf(block.Text) : RtfText(block.Kind==MarkdownBlockKind.Rule ? "────────" : block.Text)).Append("\\par\\b0\\i0\\f0\\fs22 ");
            }
        }
        return b.Append('}').ToString();
    }
    private static IEnumerable<XElement> WordNodes(MarkdownInlineNode n, bool bold=false, bool italic=false, bool strike=false, bool code=false)
    {
        bold |= n.Kind is MarkdownInlineKind.Strong or MarkdownInlineKind.StrongEmphasis;
        italic |= n.Kind is MarkdownInlineKind.Emphasis or MarkdownInlineKind.StrongEmphasis;
        strike |= n.Kind == MarkdownInlineKind.Strikethrough; code |= n.Kind == MarkdownInlineKind.Code;
        if(n.Children is not null) return n.Children.SelectMany(c=>WordNodes(c,bold,italic,strike,code));
        var run = new XElement(W+"r");var props = new XElement(W+"rPr");if(bold)props.Add(new XElement(W+"b"));if(italic)props.Add(new XElement(W+"i"));if(strike)props.Add(new XElement(W+"strike"));if(code)props.Add(new XElement(W+"rFonts",new XAttribute(W+"ascii","Consolas"),new XAttribute(W+"hAnsi","Consolas")));if(props.HasElements)run.Add(props);
        string[] lines=n.Text.Replace("\r\n","\n").Split('\n');for(int i=0;i<lines.Length;i++){if(i>0)run.Add(new XElement(W+"br"));var parts=lines[i].Split('\t');for(int c=0;c<parts.Length;c++){if(c>0)run.Add(new XElement(W+"tab"));run.Add(new XElement(W+"t",new XAttribute(XNamespace.Xml+"space","preserve"),parts[c]));}}
        return [run];
    }
    private static XElement WordParagraph(string text,int heading=0,bool literal=false)
    {
        var p=new XElement(W+"p");if(heading>0)p.Add(new XElement(W+"pPr",new XElement(W+"pStyle",new XAttribute(W+"val","Heading"+heading))));
        p.Add((literal ? new[]{new MarkdownInlineNode(MarkdownInlineKind.Code,text)} : Nodes(text)).SelectMany(n=>WordNodes(n)));return p;
    }
    private static XDocument Word(MarkdownPreviewResult model,PageLayoutSettings p)
    {
        var body=new XElement(W+"body");foreach(var b in model.Blocks)
        {
            if(b.Kind==MarkdownBlockKind.Table)
            {
                var table=new XElement(W+"tbl",new XElement(W+"tblPr",new XElement(W+"tblBorders",new[]{"top","left","bottom","right","insideH","insideV"}.Select(x=>new XElement(W+x,new XAttribute(W+"val","single"),new XAttribute(W+"sz","4"))))));
                foreach(var row in Cells(b))table.Add(new XElement(W+"tr",row.Select(c=>new XElement(W+"tc",WordParagraph(c)))));body.Add(table);
            }
            else if(b.Kind is MarkdownBlockKind.BulletList or MarkdownBlockKind.NumberedList)
            {int i=0;foreach(var item in b.Rows ?? [])body.Add(WordParagraph((b.Kind==MarkdownBlockKind.BulletList ? "• " : $"{++i}. ")+item));}
            else body.Add(WordParagraph(b.Kind==MarkdownBlockKind.Rule?"────────":b.Text,b.HeadingLevel,b.Kind==MarkdownBlockKind.Code));
        }
        body.Add(new XElement(W+"sectPr",new XElement(W+"pgSz",new XAttribute(W+"w",Twips(p.WidthMm)),new XAttribute(W+"h",Twips(p.HeightMm)),new XAttribute(W+"orient",p.Landscape?"landscape":"portrait")),new XElement(W+"pgMar",new XAttribute(W+"left",Twips(p.LeftMarginMm)),new XAttribute(W+"right",Twips(p.RightMarginMm)),new XAttribute(W+"top",Twips(p.TopMarginMm)),new XAttribute(W+"bottom",Twips(p.BottomMarginMm)))));
        return new XDocument(new XElement(W+"document",new XAttribute(XNamespace.Xmlns+"w",W),body));
    }
    private static IEnumerable<object> OdtNodes(MarkdownInlineNode n)
    {
        IEnumerable<object> value=n.Children is null ? OdtLiteral(n.Text) : n.Children.SelectMany(OdtNodes);
        string? style=n.Kind switch {MarkdownInlineKind.Strong=>"Bold",MarkdownInlineKind.Emphasis=>"Italic",MarkdownInlineKind.StrongEmphasis=>"BoldItalic",MarkdownInlineKind.Strikethrough=>"Strike",MarkdownInlineKind.Code=>"Code",_=>null};
        return style is null?value:[new XElement(T+"span",new XAttribute(T+"style-name",style),value)];
    }
    private static IEnumerable<object> OdtLiteral(string text)
    {
        // Explicit whitespace nodes prevent ODF from collapsing source spaces/tabs.
        var result=new List<object>();var plain=new StringBuilder();void Flush(){if(plain.Length>0){result.Add(plain.ToString());plain.Clear();}}
        for(int i=0;i<text.Length;i++){char c=text[i];if(c==' '){Flush();int count=1;while(i+1<text.Length&&text[i+1]==' '){count++;i++;}result.Add(new XElement(T+"s",new XAttribute(T+"c",count)));}else if(c=='\t'){Flush();result.Add(new XElement(T+"tab"));}else if(c=='\n'){Flush();result.Add(new XElement(T+"line-break"));}else if(c!='\r')plain.Append(c);}Flush();return result;
    }
    private static XElement OdtParagraph(string text,int heading=0,bool literal=false)=>new(T+(heading>0?"h":"p"),heading>0?new XAttribute(T+"outline-level",heading):null,(literal?new[]{new MarkdownInlineNode(MarkdownInlineKind.Code,text)}:Nodes(text)).SelectMany(OdtNodes));
    private static XDocument Odt(MarkdownPreviewResult model)
    {
        var text=new XElement(O+"text");int number=0;foreach(var b in model.Blocks)
        {
            if(b.Kind==MarkdownBlockKind.Table){var rows=Cells(b).ToArray();text.Add(new XElement(A+"table",new XAttribute(A+"name","Table"+(++number)),new XElement(A+"table-column",new XAttribute(A+"number-columns-repeated",rows[0].Count)),rows.Select(row=>new XElement(A+"table-row",row.Select(c=>new XElement(A+"table-cell",new XAttribute(O+"value-type","string"),OdtParagraph(c)))))));}
            else if(b.Kind is MarkdownBlockKind.BulletList or MarkdownBlockKind.NumberedList){int i=0;foreach(var item in b.Rows??[])text.Add(OdtParagraph((b.Kind==MarkdownBlockKind.BulletList?"• ":$"{++i}. ")+item));}
            else text.Add(OdtParagraph(b.Kind==MarkdownBlockKind.Rule?"────────":b.Text,b.HeadingLevel,b.Kind==MarkdownBlockKind.Code));
        }
        var styles=new XElement(O+"automatic-styles");foreach(string name in new[]{"Bold","Italic","BoldItalic","Strike","Code"})
        {
            var props=new XElement(S+"text-properties");if(name.Contains("Bold"))props.Add(new XAttribute(F+"font-weight","bold"));if(name.Contains("Italic"))props.Add(new XAttribute(F+"font-style","italic"));if(name=="Strike")props.Add(new XAttribute(S+"text-line-through-style","solid"));if(name=="Code")props.Add(new XAttribute(F+"font-family","Consolas"));styles.Add(new XElement(S+"style",new XAttribute(S+"name",name),new XAttribute(S+"family","text"),props));
        }
        return new XDocument(new XElement(O+"document-content",new XAttribute(O+"version","1.3"),new XAttribute(XNamespace.Xmlns+"office",O),new XAttribute(XNamespace.Xmlns+"text",T),new XAttribute(XNamespace.Xmlns+"table",A),new XAttribute(XNamespace.Xmlns+"style",S),new XAttribute(XNamespace.Xmlns+"fo",F),styles,new XElement(O+"body",text)));
    }
    private static void Entry(ZipArchive zip,string name,string content,CompressionLevel level=CompressionLevel.Optimal)
    {var entry=zip.CreateEntry(name,level);using var writer=new StreamWriter(entry.Open(),Utf8);writer.Write(content);}
    private static void WritePackage(Stream output,MarkdownPreviewResult model,PageLayoutSettings p,bool word)
    {
        using var zip=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:true);
        if(word)
        {
            Entry(zip,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
            Entry(zip,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Entry(zip,"word/_rels/document.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"s1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            var styles=new XElement(W+"styles",new XAttribute(XNamespace.Xmlns+"w",W));for(int h=1;h<=6;h++)styles.Add(new XElement(W+"style",new XAttribute(W+"type","paragraph"),new XAttribute(W+"styleId","Heading"+h),new XElement(W+"name",new XAttribute(W+"val","heading "+h)),new XElement(W+"pPr",new XElement(W+"outlineLvl",new XAttribute(W+"val",h-1))),new XElement(W+"rPr",new XElement(W+"b"),new XElement(W+"sz",new XAttribute(W+"val",40-h*2)))));
            Entry(zip,"word/styles.xml",styles.ToString(SaveOptions.DisableFormatting));Entry(zip,"word/document.xml",Word(model,p).ToString(SaveOptions.DisableFormatting));
        }
        else
        {
            const string mime="application/vnd.oasis.opendocument.text";Entry(zip,"mimetype",mime,CompressionLevel.NoCompression);
            Entry(zip,"content.xml",Odt(model).ToString(SaveOptions.DisableFormatting));
            var layout=new XElement(S+"page-layout",new XAttribute(S+"name","Page"),new XElement(S+"page-layout-properties",new XAttribute(F+"page-width",N(p.WidthMm)+"mm"),new XAttribute(F+"page-height",N(p.HeightMm)+"mm"),new XAttribute(S+"print-orientation",p.Landscape?"landscape":"portrait"),new XAttribute(F+"margin-left",N(p.LeftMarginMm)+"mm"),new XAttribute(F+"margin-right",N(p.RightMarginMm)+"mm"),new XAttribute(F+"margin-top",N(p.TopMarginMm)+"mm"),new XAttribute(F+"margin-bottom",N(p.BottomMarginMm)+"mm")));
            var styles=new XElement(O+"document-styles",new XAttribute(O+"version","1.3"),new XAttribute(XNamespace.Xmlns+"office",O),new XAttribute(XNamespace.Xmlns+"style",S),new XAttribute(XNamespace.Xmlns+"fo",F),new XElement(O+"automatic-styles",layout),new XElement(O+"master-styles",new XElement(S+"master-page",new XAttribute(S+"name","Standard"),new XAttribute(S+"page-layout-name","Page"))));Entry(zip,"styles.xml",styles.ToString(SaveOptions.DisableFormatting));
            Entry(zip,"META-INF/manifest.xml",new XElement(M+"manifest",new XAttribute(M+"version","1.3"),new XAttribute(XNamespace.Xmlns+"manifest",M),new XElement(M+"file-entry",new XAttribute(M+"full-path","/"),new XAttribute(M+"media-type",mime)),new[]{"content.xml","styles.xml"}.Select(n=>new XElement(M+"file-entry",new XAttribute(M+"full-path",n),new XAttribute(M+"media-type","text/xml")))).ToString(SaveOptions.DisableFormatting));
        }
    }
}
