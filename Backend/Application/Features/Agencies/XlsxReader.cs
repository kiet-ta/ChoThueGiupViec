using System.IO.Compression;
using System.Xml.Linq;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// A minimal <c>.xlsx</c> reader for the worker import template (decisions Q18): a workbook is a zip of XML parts, so it needs only the
/// .NET base library (<c>System.IO.Compression</c>, <c>System.Xml.Linq</c>), no package (AGENTS.md rule 5), like <c>XlsxWriter</c>.
/// Reads the first sheet as text: shared strings, inline strings and numbers; blank cells are empty strings. Hostile input is
/// bounded: the zip entries it reads are capped in size and the sheet in rows and columns.
/// </summary>
public static class XlsxReader
{
    /// <summary>provenance: the largest workbook part read (uncompressed); an import of a few hundred rows is far below it.</summary>
    public const long MaxPartBytes = 8 * 1024 * 1024;

    /// <summary>provenance: a sheet longer than this is not an import file; it also bounds memory.</summary>
    public const int MaxRows = 2000;

    public const int MaxColumns = 50;

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Rows of the first sheet, each as its cells' text from column A. Throws <see cref="FormatException"/> for anything that is not a readable workbook.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ReadFirstSheet(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var shared = ReadSharedStrings(zip);
            var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
                ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                    && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                ?? throw new FormatException("The workbook has no worksheet.");

            var sheet = Load(sheetEntry);
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in sheet.Descendants(Main + "row"))
            {
                if (rows.Count >= MaxRows)
                {
                    throw new FormatException($"The sheet has more than {MaxRows} rows.");
                }

                var cells = new List<string>();
                foreach (var cell in row.Elements(Main + "c"))
                {
                    var column = ColumnIndex((string?)cell.Attribute("r"));
                    if (column >= MaxColumns)
                    {
                        throw new FormatException($"The sheet has more than {MaxColumns} columns.");
                    }

                    while (cells.Count < column)
                    {
                        cells.Add(string.Empty);
                    }

                    cells.Add(CellText(cell, shared));
                }

                rows.Add(cells);
            }

            return rows;
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("The file is not a valid .xlsx workbook.", ex);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new FormatException("The workbook XML is not valid.", ex);
        }
    }

    private static string CellText(XElement cell, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        }

        var value = cell.Element(Main + "v")?.Value ?? string.Empty;
        if (type == "s")
        {
            return int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? shared[index] : string.Empty;
        }

        return value;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        return Load(entry).Descendants(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static XDocument Load(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
        {
            throw new FormatException("A part of the workbook is too large.");
        }

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    /// <summary><c>A1</c> -&gt; 0, <c>B7</c> -&gt; 1, <c>AA3</c> -&gt; 26; a cell without a reference counts as column 0.</summary>
    public static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var index = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                index = (index * 26) + (ch - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return Math.Max(0, index - 1);
    }
}
