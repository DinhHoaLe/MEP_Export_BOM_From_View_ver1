using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using MEP_Export_BOM_From_View_ver1;

internal static class SmokeTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static int checks;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }

    private static void ExpectFailure(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch (InvalidOperationException) { failed = true; }
        Check(failed, message);
    }

    private static ElementRecord Record(int id, int type, string system, string sizeKey, double quantity)
    {
        return new ElementRecord { ElementId = id, CategoryId = 1, TypeId = type, Category = "Ống & phụ kiện", Family = "Pipe",
            Type = "Standard", System = system, Size = "D=25 mm", SizeKey = sizeKey, Quantity = quantity, Unit = "m", Note = "" };
    }

    public static int Main()
    {
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            Directory.CreateDirectory(folder);
            var result = new BomResult { Model = "Mô hình thử nghiệm", Scope = "EntireModel" };
            result.Elements.Add(Record(12, 10, "CHW", "exact-size-a", 1.23456));
            result.Elements.Add(Record(11, 10, "CHW", "exact-size-a", 2.34567));
            result.Elements.Add(Record(13, 10, "CHW", "exact-size-b", 3));
            result.Elements.Add(Record(14, 11, "CHW", "exact-size-a", 4));
            result.Elements.Add(Record(15, 10, "HWS", "exact-size-a", 5));
            result.Rows = BomAggregator.Aggregate(result.Elements);
            var combined = result.Rows.Single(r => r.ElementCount == 2);
            Check(result.Rows.Count == 4, "Different exact sizes, type IDs and systems must stay separate.");
            Check(Math.Abs(combined.Quantity - 3.58023) < 1e-10, "Length aggregation lost precision.");
            Check(combined.ElementIds == "11, 12", "Audit IDs must be retained and ordered.");
            result.Warnings.Add("Full element quantities. Host model only.");
            string basic = Path.Combine(folder, "Smoke_BOM.xlsx");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            ExcelExporter.Export(basic, result);
            using (var archive = ZipFile.OpenRead(basic))
            {
                ValidatePackage(archive);
                var bom = Read(archive, SheetPath(archive, "BOM"));
                Check(bom.Descendants(S + "t").Any(t => t.Value == result.Model), "Unicode metadata lost.");
                Check(bom.Descendants(S + "v").Any(v => v.Value == "3.58023"), "Numeric values must use invariant culture and full precision.");
                var elements = Read(archive, SheetPath(archive, "Elements"));
                Check(elements.Descendants(S + "row").Count() == 10, "Elements audit sheet is incomplete.");
            }

            string template = Path.Combine(folder, "Blank_Template.xlsx");
            File.Copy(basic, template, true);
            using (var archive = ZipFile.Open(template, ZipArchiveMode.Update))
            {
                string path = SheetPath(archive, "BOM");
                var bom = Read(archive, path);
                var data = bom.Root.Element(S + "sheetData");
                data.Elements(S + "row").Where(r => (int)r.Attribute("r") >= 6).Remove();
                data.Add(new XElement(S + "row", new XAttribute("r", 6), new XAttribute("ht", 25), new XAttribute("customHeight", 1),
                    new XElement(S + "c", new XAttribute("r", "H6"), new XAttribute("s", 2)),
                    new XElement(S + "c", new XAttribute("r", "K6"), new XElement(S + "f", "SUM(H6:H100)"))));
                Write(archive, path, bom);
            }
            byte[] source = File.ReadAllBytes(template);
            string custom = Path.Combine(folder, "Template_Output.xlsx");
            ExcelExporter.Export(custom, result, template, "BOM", 6);
            Check(source.SequenceEqual(File.ReadAllBytes(template)), "Source template changed.");
            using (var archive = ZipFile.OpenRead(custom))
            {
                ValidatePackage(archive);
                var bom = Read(archive, SheetPath(archive, "BOM"));
                Check(bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "K6").Element(S + "f").Value == "SUM(H6:H100)", "Formula outside output range was lost.");
                Check((string)bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "H9").Attribute("s") == "2", "Seed-row formatting was not copied.");
                Check((string)bom.Descendants(S + "row").Single(r => (int)r.Attribute("r") == 9).Attribute("ht") == "25", "Row height was not copied.");
                Check(Read(archive, SheetPath(archive, "BOM_Elements")).Descendants(S + "c").All(c => c.Attribute("s") == null), "Appended audit sheet must not assume template style IDs.");
            }
            ExpectFailure(() => ExcelExporter.Export(template, result, template), "Overwriting template must be rejected.");
            byte[] before = File.ReadAllBytes(custom);
            ExpectFailure(() => ExcelExporter.Export(custom, result, basic, "BOM"), "Nonblank template area must be rejected.");
            Check(before.SequenceEqual(File.ReadAllBytes(custom)), "Failed export damaged existing output.");
            ExpectFailure(() => ExcelExporter.Export(custom, result, template, "Missing"), "Missing worksheet must be rejected.");
            ExpectFailure(() => ExcelExporter.Export(custom, result, "", "", 1048576), "Row overflow must be rejected.");

            using (var archive = ZipFile.Open(template, ZipArchiveMode.Update))
            {
                string path = SheetPath(archive, "BOM");
                var bom = Read(archive, path);
                bom.Root.Add(new XElement(S + "mergeCells", new XAttribute("count", 1), new XElement(S + "mergeCell", new XAttribute("ref", "A6:B6"))));
                Write(archive, path, bom);
            }
            ExpectFailure(() => ExcelExporter.Export(custom, result, template), "Overlapping merged data cells must be rejected.");

            var textResult = new BomResult { Model = "Text", Scope = "Test" };
            var textRecord = Record(1, 1, "=HYPERLINK(\"unsafe\")", "a", 1);
            textRecord.Type = "Vietnamese: Ống\u0001";
            textResult.Elements.Add(textRecord); textResult.Rows = BomAggregator.Aggregate(textResult.Elements);
            textResult.Rows[0].ElementIds = new string('x', 40000);
            string textFile = Path.Combine(folder, "Text_Limits.xlsx");
            ExcelExporter.Export(textFile, textResult);
            using (var archive = ZipFile.OpenRead(textFile))
            {
                var bom = Read(archive, SheetPath(archive, "BOM"));
                Check(bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "J6").Descendants(S + "t").Single().Value.Length == 32767, "Excel cell length limit was exceeded.");
                var formulaText = bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "E6");
                Check((string)formulaText.Attribute("t") == "inlineStr" && formulaText.Element(S + "f") == null, "User data was interpreted as a formula.");
                Check(bom.Descendants(S + "t").Any(t => t.Value == "Vietnamese: Ống"), "Invalid XML characters were not sanitized.");
            }
            var modified = new BomResult { Model = "Modified", Scope = "EntireModel" };
            var p1 = Record(101, 10, "CHW", "a", 1.5); p1.ExtraValues["param:comments"] = "A";
            var p2 = Record(102, 10, "CHW", "a", 2.5); p2.ExtraValues["param:comments"] = "B";
            p1.Category = p2.Category = "Category / with [invalid] characters and a very long title";
            var p3 = Record(103, 10, "CHW", "a", 3); p3.CategoryId = 2;
            p3.Category = "Category / with [invalid] characters and a very long title 2";
            var p4 = Record(104, 10, "CHW", "a", 4); p4.CategoryId = 3; p4.Category = "BOM";
            modified.Elements.AddRange(new[] { p1, p2, p3, p4 }); modified.Rows = BomAggregator.Aggregate(modified.Elements);
            Check(modified.Rows.Single(r => r.ElementCount == 2).ExtraValues["param:comments"] == "<varies>", "Mixed parameter values must not be misreported.");
            var required = modified.Layout.Columns.Single(c => c.Key == "Category"); required.IsVisible = false;
            Check(required.IsVisible && !required.CanHide, "Required fields must remain visible.");
            var badLayout = modified.Layout.Clone(); badLayout.Columns.RemoveAll(c => c.Key == "Quantity");
            ExpectFailure(() => badLayout.Validate(), "Required field removal must fail validation.");
            modified.Layout.Title = "Custom BOM title";
            var quantity = modified.Layout.Columns.Single(c => c.Key == "Quantity"); quantity.Header = "Measured length";
            modified.Layout.Columns.Remove(quantity); modified.Layout.Columns.Insert(0, quantity);
            modified.Layout.Columns.Single(c => c.Key == "System").IsVisible = false;
            modified.Layout.Columns.Add(new BomColumn { Key = "param:comments", Header = "Remarks" });
            string multi = Path.Combine(folder, "Categories_Custom_Layout.xlsx"); ExcelExporter.Export(multi, modified);
            using (var archive = ZipFile.OpenRead(multi))
            {
                ValidatePackage(archive);
                var sheets = Read(archive, "xl/workbook.xml").Descendants(S + "sheet").Select(e => (string)e.Attribute("name")).ToList();
                Check(sheets.Count == 6 && sheets.All(n => n.Length <= 31 && n.IndexOfAny("\\/:?*[]".ToCharArray()) < 0), "Category sheet names must be valid and unique.");
                Check(sheets.Distinct(StringComparer.OrdinalIgnoreCase).Count() == sheets.Count && sheets.Contains("BOM_2"), "Sheet collisions must be resolved.");
                var bom = Read(archive, SheetPath(archive, "BOM"));
                Check(bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "A1").Descendants(S + "t").Single().Value == "Custom BOM title", "Custom title was lost.");
                Check(bom.Descendants(S + "c").Single(c => (string)c.Attribute("r") == "A5").Descendants(S + "t").Single().Value == "Measured length", "Custom column order/heading was lost.");
                Check(bom.Descendants(S + "row").Single(r => (int)r.Attribute("r") == 5).Descendants(S + "t").All(t => t.Value != "System"), "Hidden field was exported.");
                Check(bom.Descendants(S + "t").Any(t => t.Value == "<varies>"), "Additional parameter was not exported.");
                foreach (var category in sheets.Skip(3))
                {
                    var categorySheet = Read(archive, SheetPath(archive, category));
                    Check(categorySheet.Descendants(S + "row").Count(r => (int)r.Attribute("r") >= 6) == 1, "Category sheet includes another category or incorrect grouping.");
                }
            }
            var names = new[] { new ScheduleRequest { CategoryId = 1, CategoryName = "Pipes", ScheduleName = "Pipes BOM" },
                new ScheduleRequest { CategoryId = 2, CategoryName = "Ducts", ScheduleName = "Ducts BOM" } };
            ScheduleNames.Validate(names, new string[0]); Check(true, "Valid schedule names rejected.");
            names[1].ScheduleName = "pipes bom"; ExpectFailure(() => ScheduleNames.Validate(names, new string[0]), "Duplicate batch name must fail.");
            names[1].ScheduleName = "Ducts BOM"; ExpectFailure(() => ScheduleNames.Validate(names, new[] { "PIPES BOM" }), "Existing schedule name must fail.");
            names[1].ScheduleName = ""; ExpectFailure(() => ScheduleNames.Validate(names, new string[0]), "Blank schedule name must fail.");
            names[1].ScheduleName = "Ducts: BOM"; ExpectFailure(() => ScheduleNames.Validate(names, new string[0]), "Invalid schedule name must fail.");
            string noSplit = Path.Combine(folder, "No_Category_Sheets.xlsx"); ExcelExporter.Export(noSplit, modified, categorySheets: false);
            using (var archive = ZipFile.OpenRead(noSplit)) Check(Read(archive, "xl/workbook.xml").Descendants(S + "sheet").Count() == 3, "Category sheets option did not switch off.");
            Console.WriteLine("PASS: " + checks + " checks (BOM, XLSX/template, category sheets, column layout, required fields and schedule names).");
            Console.WriteLine("Fixtures: " + folder);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void ValidatePackage(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
            using (var stream = entry.Open()) XDocument.Load(stream);
        var workbook = Read(archive, "xl/workbook.xml");
        foreach (var sheet in workbook.Descendants(S + "sheet"))
            Check(archive.GetEntry(SheetPath(archive, (string)sheet.Attribute("name"))) != null, "Missing worksheet part.");
    }

    private static string SheetPath(ZipArchive archive, string name)
    {
        var sheet = Read(archive, "xl/workbook.xml").Descendants(S + "sheet").Single(e => (string)e.Attribute("name") == name);
        var relationship = Read(archive, "xl/_rels/workbook.xml.rels").Descendants(P + "Relationship").Single(e => (string)e.Attribute("Id") == (string)sheet.Attribute(R + "id"));
        return "xl/" + (string)relationship.Attribute("Target");
    }

    private static XDocument Read(ZipArchive archive, string path)
    { using (var stream = archive.GetEntry(path).Open()) return XDocument.Load(stream); }

    private static void Write(ZipArchive archive, string path, XDocument document)
    { archive.GetEntry(path).Delete(); using (var stream = archive.CreateEntry(path).Open()) document.Save(stream); }
}
