using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using AnnabaDigital.Application.Users;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// RPT-003. Turns a CSV export into a one-sheet Excel workbook (.xlsx) with no third-party library: a zip of five small XML files.
/// Plain amounts become numbers (so they can be summed); everything else stays text, in particular identifiers with leading zeros or more than 15 digits (NIK, NPWP, account numbers),
/// which Excel would otherwise mangle. Text is written as inline strings, which a spreadsheet never evaluates, so a cell starting with "=" is shown, not run.
/// </summary>
public static partial class SimpleXlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    [GeneratedRegex(@"^-?(0|[1-9]\d{0,14})(\.\d{1,6})?$")] private static partial Regex Number();

    public static ExportFile FromCsv(ExportFile csv)
    {
        var text = Encoding.UTF8.GetString(csv.Bytes).TrimStart('﻿');
        var name = csv.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? csv.FileName[..^4] + ".xlsx" : csv.FileName + ".xlsx";
        return new ExportFile(name, ContentType, Build(UserImportService.ParseCsv(text)));
    }

    public static byte[] Build(IReadOnlyList<List<string>> rows)
    {
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        var cols = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (cols > 0) { sheet.Append("<cols>"); for (var c = 1; c <= cols; c++) sheet.Append(CultureInfo.InvariantCulture, $"<col min=\"{c}\" max=\"{c}\" width=\"{Math.Clamp(rows.Max(r => c <= r.Count ? r[c - 1].Length : 0) + 2, 8, 50)}\" customWidth=\"1\"/>"); sheet.Append("</cols>"); }
        sheet.Append("<sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var v = rows[r][c]; if (v.Length == 0) continue; var cell = Ref(c, r);
                var plain = v.Length > 1 && v[0] == '\'' ? v[1..] : v;                                     // the CSV writer prefixes risky cells (and negative amounts) with an apostrophe
                if (r > 0 && Number().IsMatch(plain)) sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{cell}\"><v>{plain}</v></c>");
                else sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"{cell}\" t=\"inlineStr\"{(r == 0 ? " s=\"1\"" : "")}><is><t xml:space=\"preserve\">{Clean(v)}</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml) { var e = zip.CreateEntry(path, CompressionLevel.Fastest); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(xml); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs></styleSheet>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    private static string Ref(int col, int row) { var s = ""; for (var c = col; c >= 0; c = c / 26 - 1) s = (char)('A' + c % 26) + s; return s + (row + 1).ToString(CultureInfo.InvariantCulture); }
    /// <summary>XML-escapes and drops the control characters XML 1.0 does not allow.</summary>
    private static string Clean(string v) => SecurityElement.Escape(new string(v.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ' && ch != '￾' && ch != '￿').ToArray())) ?? "";
}
