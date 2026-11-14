# TUR 36 | Unsigned UDF export prototype

EXP-007 is in review. `Embasic.ENote.Export.UdfWriter` creates a new UDF
1.8 candidate from explicit paragraphs and the existing A4 geometry model.
The project uses only .NET and the existing ENote Core dependency. The ZIP
contains UTF-8 `content.xml` with a CDATA text pool, paragraph offsets and
lengths, a default font reference and A4 portrait margins in points. The
output contains no signature data and embeds no fonts.

The prototype accepts BMP text, blank paragraphs and whole-paragraph bold.
Control characters, embedded line breaks, supplementary Unicode, oversized
input and unsupported paper settings are rejected before writing. A copy is
published using a same-directory temporary file and a non-overwriting move.
The WPF and Markdown editing paths do not call this writer.

Create a synthetic fixture from a clean checkout:

```sh
dotnet run --project tests/Embasic.ENote.UdfWriter.Tests -c Release -- --sample ./enote-synthetic.udf
```

Open the fixture in [the official web editor](https://udf.adalet.gov.tr/)
or in [UYAP Document Editor 5.4.20](https://www.uyap.gov.tr/Uyap-Editor).
Check Turkish text, the blank paragraph, bold and A4 portrait. Add `YENİ`
to the last paragraph, save to a new file and reopen it. Compare the
content and page layout. Keep evidence for each platform separately.
Tables, images, font fidelity, supplementary Unicode and production
integration have separate acceptance criteria. The Windows desktop round
trip and physical print acceptance remain open.

[UDF Toolkit format notes](https://github.com/saidsurucu/UDF-Toolkit/blob/main/Docs.md)
informed the initial wire-format candidate. No third-party implementation
or assets are included.
