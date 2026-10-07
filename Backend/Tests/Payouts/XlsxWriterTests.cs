using System.IO.Compression;
using System.Xml.Linq;
using CommonService.Application.Features.Payouts;

namespace CommonService.Tests.Payouts;

/// <summary>Reads back what <see cref="XlsxWriter"/> wrote, part by part, so the tests check the file and not the writer's own state.</summary>
internal static class XlsxTestReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    internal sealed record Cell(string Reference, string? Type, string? Style, string? Value);

    public static List<string> PartNames(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    public static XDocument Part(byte[] file, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream); // throws when the part is not well-formed XML
    }

    /// <summary>The rows of the sheet: each row is its cells in column order (a missing cell is simply absent).</summary>
    public static List<List<Cell>> Rows(byte[] file) =>
        Part(file, "xl/worksheets/sheet1.xml").Descendants(Main + "row")
            .Select(row => row.Elements(Main + "c").Select(c => new Cell(
                (string)c.Attribute("r")!,
                (string?)c.Attribute("t"),
                (string?)c.Attribute("s"),
                c.Element(Main + "is") is { } inline ? inline.Element(Main + "t")!.Value : c.Element(Main + "v")?.Value)).ToList())
            .ToList();

    /// <summary>The sheet as plain text: strings as written, numbers as written, a missing cell as null.</summary>
    public static List<string?[]> Table(byte[] file, int columns) =>
        Rows(file).Select(row =>
        {
            var values = new string?[columns];
            foreach (var cell in row)
            {
                var letters = new string(cell.Reference.TakeWhile(char.IsLetter).ToArray());
                var index = letters.Aggregate(0, (acc, ch) => acc * 26 + (ch - 'A' + 1)) - 1;
                values[index] = cell.Value;
            }

            return values;
        }).ToList();

    public static string SheetName(byte[] file) =>
        (string)Part(file, "xl/workbook.xml").Descendants(Main + "sheet").Single().Attribute("name")!;
}

/// <summary>BE-M6-05: the minimal .xlsx writer (no package; zip + XML from the .NET base library).</summary>
public class XlsxWriterTests
{
    private static byte[] Build(IReadOnlyList<string> headers, params object?[][] rows) =>
        XlsxWriter.Build("Test", headers, rows.Select(r => (IReadOnlyList<object?>)r).ToList());

    [Fact]
    public void The_file_is_a_zip_with_exactly_the_parts_of_a_one_sheet_workbook_and_the_content_types_part_comes_first()
    {
        var file = Build(["a"], ["x"]);

        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml"],
            XlsxTestReader.PartNames(file));
        Assert.Equal((byte)'P', file[0]); // a zip starts with "PK"
        Assert.Equal((byte)'K', file[1]);
    }

    [Fact]
    public void Every_part_is_well_formed_XML_and_the_relationships_and_content_types_point_at_real_parts()
    {
        var file = Build(["a"], ["x"]);
        var names = XlsxTestReader.PartNames(file);

        foreach (var name in names) Assert.NotNull(XlsxTestReader.Part(file, name).Root);

        var types = XlsxTestReader.Part(file, "[Content_Types].xml").Root!;
        var overrides = types.Elements().Where(e => e.Name.LocalName == "Override").Select(e => ((string)e.Attribute("PartName")!).TrimStart('/')).ToList();
        Assert.All(overrides, p => Assert.Contains(p, names));
        Assert.Contains(types.Elements(), e => e.Name.LocalName == "Default" && (string?)e.Attribute("Extension") == "rels");

        var rootRel = XlsxTestReader.Part(file, "_rels/.rels").Root!.Elements().Single();
        Assert.EndsWith("/officeDocument", (string)rootRel.Attribute("Type")!);
        Assert.Contains((string)rootRel.Attribute("Target")!, names);

        var workbookRels = XlsxTestReader.Part(file, "xl/_rels/workbook.xml.rels").Root!.Elements().ToList();
        Assert.Equal(2, workbookRels.Count);
        Assert.All(workbookRels, r => Assert.Contains("xl/" + (string)r.Attribute("Target")!, names));
    }

    [Fact]
    public void The_header_row_is_bold_text_and_cells_are_typed_by_their_value()
    {
        var file = Build(["name", "amount", "id"], ["Nguyễn Văn A", 1234567m, 987654321012L]);

        var rows = XlsxTestReader.Rows(file);
        Assert.Equal(2, rows.Count);

        Assert.All(rows[0], c => { Assert.Equal("inlineStr", c.Type); Assert.Equal("1", c.Style); }); // bold header
        Assert.Equal(["name", "amount", "id"], rows[0].Select(c => c.Value).ToArray());
        Assert.Equal(["A1", "B1", "C1"], rows[0].Select(c => c.Reference).ToArray());

        var data = rows[1];
        Assert.Equal(("inlineStr", "Nguyễn Văn A"), (data[0].Type, data[0].Value)); // text keeps its accents
        Assert.Equal((null, "2", "1234567"), (data[1].Type, data[1].Style, data[1].Value)); // money: number with thousands separators
        Assert.Equal((null, null, "987654321012"), (data[2].Type, data[2].Style, data[2].Value)); // ids are plain numbers
    }

    [Fact]
    public void An_account_number_stays_text_so_leading_zeros_survive_and_a_missing_value_is_an_empty_cell()
    {
        var file = Build(["bank_name", "bank_account_no"], [null, "0012345678"]);

        var cells = XlsxTestReader.Rows(file)[1];
        var only = Assert.Single(cells);
        Assert.Equal("B2", only.Reference);
        Assert.Equal("inlineStr", only.Type);
        Assert.Equal("0012345678", only.Value);
    }

    [Fact]
    public void Decimals_are_written_with_the_invariant_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("vi-VN"); // decimal comma
            var file = Build(["amount"], [1234.5m]);

            Assert.Equal("1234.5", XlsxTestReader.Rows(file)[1][0].Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void XML_special_characters_are_escaped_control_characters_dropped_and_emoji_kept()
    {
        var file = Build(["t"], ["a & b < c > d \"q\" 'p' \u0001\u0008 end 😀"]);

        Assert.Equal("a & b < c > d \"q\" 'p'  end 😀", XlsxTestReader.Rows(file)[1][0].Value);
    }

    [Fact]
    public void A_lone_surrogate_is_dropped_so_the_file_stays_readable()
    {
        var file = Build(["t"], ["x\uD800y"]);

        Assert.Equal("xy", XlsxTestReader.Rows(file)[1][0].Value);
    }

    [Fact]
    public void Surrounding_spaces_are_preserved_in_text_cells()
    {
        var file = Build(["t"], ["  padded  "]);

        Assert.Equal("  padded  ", XlsxTestReader.Rows(file)[1][0].Value);
    }

    [Fact]
    public void The_sheet_name_is_made_safe_and_shown_in_the_workbook()
    {
        var file = XlsxWriter.Build("Agency [summary]: 2026/10?", ["a"], []);

        Assert.Equal("Agency summary 202610", XlsxTestReader.SheetName(file));
    }

    [Theory]
    [InlineData("Freelancer", "Freelancer")]
    [InlineData("[]:*?/\\", "Sheet1")]
    [InlineData("", "Sheet1")]
    [InlineData("0123456789012345678901234567890123456789", "0123456789012345678901234567890")]
    public void Sheet_names_follow_the_Excel_rules(string input, string expected)
    {
        Assert.Equal(expected, XlsxWriter.SafeSheetName(input));
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_letters_follow_the_spreadsheet_convention(int index, string expected)
    {
        Assert.Equal(expected, XlsxWriter.ColumnName(index));
    }

    [Fact]
    public void A_file_without_data_rows_has_only_the_header()
    {
        var file = Build(["a", "b"]);

        Assert.Single(XlsxTestReader.Rows(file));
    }

    [Fact]
    public void The_same_input_gives_the_same_bytes()
    {
        Assert.Equal(Build(["a", "b"], ["x", 1m]), Build(["a", "b"], ["x", 1m]));
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Build(["a", "b"], ["only one"]));
    }

    [Fact]
    public void More_than_26_columns_get_two_letter_references()
    {
        var headers = Enumerable.Range(0, 28).Select(i => "h" + i).ToList();
        var row = Enumerable.Range(0, 28).Select(i => (object?)i).ToArray();

        var file = XlsxWriter.Build("Wide", headers, [row]);

        var cells = XlsxTestReader.Rows(file)[1];
        Assert.Equal("AB2", cells[27].Reference);
        Assert.Equal("27", cells[27].Value);
    }
}
