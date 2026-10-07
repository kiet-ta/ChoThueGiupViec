using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CommonService.Application.Features.Payouts;

/// <summary>
/// A minimal <c>.xlsx</c> writer for the bank transfer files (decision Q18). A workbook is a zip of XML parts, so it needs only
/// <c>System.IO.Compression</c> and <c>System.Xml</c> from the .NET base library: no package. One sheet, a bold header row, text cells
/// as inline strings (an account number keeps its leading zeros), <see cref="decimal"/> as a number with thousands separators and
/// <see cref="int"/> / <see cref="long"/> as plain numbers (ids), <c>null</c> as an empty cell.
/// </summary>
public static class XlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const int HeaderStyle = 1;
    private const int MoneyStyle = 2;

    /// <summary>Excel limits a sheet name to 31 characters and forbids <c>[ ] : * ? / \</c>.</summary>
    public static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Sheet1";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    /// <summary>Builds the workbook; every row must have as many cells as there are headers.</summary>
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        foreach (var row in rows)
        {
            if (row.Count != headers.Count) throw new ArgumentException("Every row needs one cell per header.", nameof(rows));
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // The same input gives the same bytes: a fixed timestamp in every entry.
            Write(zip, "[Content_Types].xml", ContentTypesXml());
            Write(zip, "_rels/.rels", RootRelsXml());
            Write(zip, "xl/workbook.xml", WorkbookXml(SafeSheetName(sheetName)));
            Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            Write(zip, "xl/styles.xml", StylesXml());
            Write(zip, "xl/worksheets/sheet1.xml", SheetXml(headers, rows));
        }

        return stream.ToArray();
    }

    /// <summary>Column letters of a 0-based index: 0 = A, 25 = Z, 26 = AA.</summary>
    public static string ColumnName(int index)
    {
        var name = new StringBuilder();
        for (var n = index + 1; n > 0; n = (n - 1) / 26) name.Insert(0, (char)('A' + ((n - 1) % 26)));
        return name.ToString();
    }

    private static void Write(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        output.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypesXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<Types xmlns=\"{ContentTypesNs}\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
        + "</Types>";

    private static string RootRelsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<Relationships xmlns=\"{PackageRelNs}\">"
        + $"<Relationship Id=\"rId1\" Type=\"{RelNs}/officeDocument\" Target=\"xl/workbook.xml\"/>"
        + "</Relationships>";

    private static string WorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<workbook xmlns=\"{MainNs}\" xmlns:r=\"{RelNs}\">"
        + $"<sheets><sheet name=\"{Escape(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
        + "</workbook>";

    private static string WorkbookRelsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<Relationships xmlns=\"{PackageRelNs}\">"
        + $"<Relationship Id=\"rId1\" Type=\"{RelNs}/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
        + $"<Relationship Id=\"rId2\" Type=\"{RelNs}/styles\" Target=\"styles.xml\"/>"
        + "</Relationships>";

    /// <summary>Style 0 default, 1 bold header, 2 number with thousands separators (built-in format 3).</summary>
    private static string StylesXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + $"<styleSheet xmlns=\"{MainNs}\">"
        + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
        + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"3\">"
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
        + "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "</cellXfs>"
        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
        + "</styleSheet>";

    private static string SheetXml(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<worksheet xmlns=\"{MainNs}\">");
        sb.Append(CultureInfo.InvariantCulture, $"<cols><col min=\"1\" max=\"{headers.Count}\" width=\"24\" customWidth=\"1\"/></cols>");
        sb.Append("<sheetData>");

        sb.Append("<row r=\"1\">");
        for (var c = 0; c < headers.Count; c++) AppendCell(sb, c, 1, headers[c], HeaderStyle);
        sb.Append("</row>");

        for (var r = 0; r < rows.Count; r++)
        {
            var rowNumber = r + 2;
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");
            for (var c = 0; c < headers.Count; c++) AppendCell(sb, c, rowNumber, rows[r][c], 0);
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, int column, int row, object? value, int textStyle)
    {
        var reference = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
        switch (value)
        {
            case null:
                return;
            case decimal money:
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{MoneyStyle}\"><v>{money.ToString("0.############################", CultureInfo.InvariantCulture)}</v></c>");
                return;
            case int or long or short or byte:
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                return;
            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                sb.Append(CultureInfo.InvariantCulture,
                    $"<c r=\"{reference}\" t=\"inlineStr\" s=\"{textStyle}\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");
                return;
        }
    }

    /// <summary>Escapes the five XML characters and drops characters XML 1.0 cannot carry.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], ch))
            {
                sb.Append(ch).Append(text[++i]); // a valid pair (an emoji, say) is kept as it is
                continue;
            }

            if (!XmlConvert.IsXmlChar(ch)) continue; // control characters and lone surrogates would make the file unreadable
            sb.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => ch.ToString(),
            });
        }

        return sb.ToString();
    }
}
