using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MEP_Export_BOM_From_View_ver1
{
    // Native .xlsx writer: no Excel installation or third-party DLL required.
    public static class ExcelExporter
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";
        private const string WorksheetType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

        public static void ExportSchedules(string outputPath, IDictionary<string, List<object[]>> schedules)
        {
            if (schedules == null || schedules.Count == 0) throw new InvalidOperationException("Select at least one schedule.");
            outputPath = Path.GetFullPath(outputPath);
            string temporary = Path.Combine(Path.GetDirectoryName(outputPath), ".schedule-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try {
                using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update)) {
                    CreatePackage(archive);
                    foreach (var schedule in schedules) {
                        if (schedule.Value.Count > 1048575 || schedule.Value.Any(r => r.Length > 16384)) throw new InvalidOperationException("Schedule exceeds Excel row/column limits.");
                        var rows = new List<XElement> { Row(1, new object[] { schedule.Key }) }; int index = 2;
                        foreach (var values in schedule.Value) { rows.Add(Row(index, values, index == 2 ? 1 : 0)); index++; }
                        int columns = Math.Max(1, schedule.Value.Select(r => r.Length).DefaultIfEmpty(1).Max());
                        var worksheet = Worksheet(rows, columns, index - 1, false);
                        var pane = worksheet.Descendants(S + "pane").Single(); pane.SetAttributeValue("ySplit", 2); pane.SetAttributeValue("topLeftCell", "A3");
                        foreach (var column in worksheet.Descendants(S + "col")) column.SetAttributeValue("width", 30);
                        AppendSheet(archive, schedule.Key, worksheet);
                    }
                }
                if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null); else File.Move(temporary, outputPath);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static void Export(string outputPath, BomResult result, string templatePath = "", string sheetName = "", int firstDataRow = 6, bool categorySheets = true)
        {
            if (result == null || result.Rows.Count == 0) throw new InvalidOperationException("No BOM data to export.");
            result.Layout.Validate();
            if (firstDataRow < 1 || (long)firstDataRow + result.Rows.Count - 1 > 1048576 || result.Elements.Count > 1048571)
                throw new InvalidOperationException("Data exceeds the Excel row limit.");
            outputPath = Path.GetFullPath(outputPath);
            bool useTemplate = !string.IsNullOrWhiteSpace(templatePath);
            if (useTemplate && string.Equals(outputPath, Path.GetFullPath(templatePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the result to a different file from the template.");
            string temporary = Path.Combine(Path.GetDirectoryName(outputPath), ".bom-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                if (useTemplate)
                {
                    if (!File.Exists(templatePath)) throw new FileNotFoundException("Template not found.", templatePath);
                    if (!string.Equals(Path.GetExtension(templatePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Use an unencrypted .xlsx template.");
                    File.Copy(templatePath, temporary);
                    using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
                    {
                        FillTemplate(archive, sheetName, firstDataRow, result);
                        AppendSheet(archive, "BOM_Elements", WithoutStyles(BuildElements(result)));
                        AppendSheet(archive, "BOM_Notes", WithoutStyles(BuildNotes(result)));
                        if (categorySheets) AppendCategories(archive, result, false);
                    }
                }
                else
                {
                    using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
                    {
                        CreatePackage(archive);
                        AppendSheet(archive, "BOM", BuildBom(result));
                        AppendSheet(archive, "Elements", BuildElements(result));
                        AppendSheet(archive, "Notes", BuildNotes(result));
                        if (categorySheets) AppendCategories(archive, result, true);
                    }
                }
                // Move into place only after the package is successfully closed.
                if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null);
                else File.Move(temporary, outputPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void CreatePackage(ZipArchive archive)
        {
            Write(archive, "[Content_Types].xml", new XDocument(new XElement(C + "Types",
                new XElement(C + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(C + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                Override("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));
            Write(archive, "_rels/.rels", new XDocument(new XElement(P + "Relationships",
                new XElement(P + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
            Write(archive, "xl/workbook.xml", new XDocument(new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets"))));
            Write(archive, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(P + "Relationships",
                new XElement(P + "Relationship", new XAttribute("Id", "rIdStyles"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")))));
            Write(archive, "xl/styles.xml", XDocument.Parse(
                "<styleSheet xmlns='" + S + "'><fonts count='2'><font><sz val='11'/><name val='Calibri'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Calibri'/></font></fonts>" +
                "<fills count='3'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF245A81'/><bgColor indexed='64'/></patternFill></fill></fills>" +
                "<borders count='1'><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs>" +
                "<cellXfs count='3'><xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'/><xf numFmtId='0' fontId='1' fillId='2' borderId='0' xfId='0' applyFont='1' applyFill='1'/><xf numFmtId='4' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1'/></cellXfs>" +
                "<cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles></styleSheet>"));
        }

        private static XElement Override(string path, string contentType)
        { return new XElement(C + "Override", new XAttribute("PartName", path), new XAttribute("ContentType", contentType)); }

        private static void AppendSheet(ZipArchive archive, string name, XDocument sheet)
        {
            var workbook = Read(archive, "xl/workbook.xml");
            var relationships = Read(archive, "xl/_rels/workbook.xml.rels");
            var contentTypes = Read(archive, "[Content_Types].xml");
            var sheets = workbook.Root.Element(S + "sheets");
            if (sheets == null) throw new InvalidOperationException("Workbook has no sheets collection.");
            var existingNames = new HashSet<string>(sheets.Elements(S + "sheet").Select(e => (string)e.Attribute("name")), StringComparer.OrdinalIgnoreCase);
            foreach (char ch in "\\/:?*[]") name = name.Replace(ch, '-');
            name = Clean(name).Trim(' ', '\'');
            if (string.IsNullOrEmpty(name)) name = "Category";
            if (name.Length > 31) name = name.Substring(0, 31);
            string baseName = name; int suffix = 2;
            while (existingNames.Contains(name))
            {
                string tail = "_" + suffix++;
                name = baseName.Substring(0, Math.Min(baseName.Length, 31 - tail.Length)) + tail;
            }
            uint id = sheets.Elements(S + "sheet").Select(e => (uint)e.Attribute("sheetId")).DefaultIfEmpty(0u).Max() + 1;
            string relationshipId = "rIdBom" + Guid.NewGuid().ToString("N");
            string part = "worksheets/bom" + Guid.NewGuid().ToString("N") + ".xml";
            sheets.Add(new XElement(S + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(R + "id", relationshipId)));
            relationships.Root.Add(new XElement(P + "Relationship", new XAttribute("Id", relationshipId), new XAttribute("Type", WorksheetType), new XAttribute("Target", part)));
            contentTypes.Root.Add(Override("/xl/" + part, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
            Write(archive, "xl/" + part, sheet);
            Write(archive, "xl/workbook.xml", workbook);
            Write(archive, "xl/_rels/workbook.xml.rels", relationships);
            Write(archive, "[Content_Types].xml", contentTypes);
        }

        private static XDocument BuildBom(BomResult result)
        {
            var columns = result.Layout.Columns.Where(c => c.IsVisible).ToList();
            var rows = Metadata(result, result.Layout.Title);
            rows.Add(Row(5, columns.Select(c => (object)c.Header).ToArray(), 1));
            int index = 6;
            foreach (var item in result.Rows) rows.Add(Row(index++, result.Layout.Values(item)));
            return Worksheet(rows, columns.Count, index - 1, true);
        }

        private static void AppendCategories(ZipArchive archive, BomResult result, bool styled)
        {
            foreach (var group in result.Elements.GroupBy(e => e.CategoryId).OrderBy(g => g.First().Category))
            {
                var subset = new BomResult { Model = result.Model, Scope = result.Scope + " / " + group.First().Category,
                    Layout = result.Layout.Clone(), Elements = group.ToList() };
                subset.Rows = result.Query == null ? BomAggregator.Aggregate(subset.Elements) : BomQuery.Apply(subset.Elements, result.Query);
                var document = BuildBom(subset);
                AppendSheet(archive, group.First().Category, styled ? document : WithoutStyles(document));
            }
        }

        private static XDocument BuildElements(BomResult result)
        {
            var rows = Metadata(result, "ELEMENT AUDIT - FULL ELEMENT QUANTITIES");
            rows.Add(Row(5, new object[] { "Element ID", "Category", "Family", "Type", "System", "Size", "Unit", "Quantity", "Note", "Source", "Link Instance ID", "Unique ID" }, 1));
            int index = 6;
            foreach (var item in result.Elements.OrderBy(e => e.ElementId))
                rows.Add(Row(index++, new object[] { item.ElementId, item.Category, item.Family, item.Type, item.System, item.Size, item.Unit, item.Quantity, item.Note, item.SourceName, item.LinkInstanceId, item.UniqueId }));
            return Worksheet(rows, 12, index - 1, true);
        }

        private static XDocument BuildNotes(BomResult result)
        {
            var rows = Metadata(result, "EXPORT NOTES");
            rows.Add(Row(5, new object[] { "No", "Note" }, 1));
            int index = 6;
            foreach (string warning in result.Warnings) rows.Add(Row(index, new object[] { index++ - 5, warning }));
            rows.Add(Row(index, new object[] { index - 5, "Element IDs in BOM cells are truncated at Excel's text limit. The Elements sheet retains each ID separately." }));
            return Worksheet(rows, 2, index, false);
        }

        private static List<XElement> Metadata(BomResult result, string title)
        {
            return new List<XElement>
            {
                Row(1, new object[] { title }), Row(2, new object[] { "Model", result.Model }),
                Row(3, new object[] { "Scope", result.Scope }),
                Row(4, new object[] { "Exported (UTC+7)", DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) })
            };
        }

        private static XDocument Worksheet(List<XElement> rows, int columns, int lastRow, bool filter, bool styled = true)
        {
            var cols = new XElement(S + "cols");
            for (int i = 1; i <= columns; i++)
                cols.Add(new XElement(S + "col", new XAttribute("min", i), new XAttribute("max", i),
                    new XAttribute("width", columns == 2 && i == 2 ? 110 : i == 1 ? 14 : i == 7 || i == 8 || i == 9 ? 16 : 30), new XAttribute("customWidth", 1)));
            if (!styled) foreach (var cell in rows.SelectMany(r => r.Elements(S + "c"))) cell.Attribute("s")?.Remove();
            var root = new XElement(S + "worksheet",
                new XElement(S + "dimension", new XAttribute("ref", "A1:" + Column(columns) + lastRow)),
                new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(S + "pane", new XAttribute("ySplit", 5), new XAttribute("topLeftCell", "A6"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(S + "sheetFormatPr", new XAttribute("defaultRowHeight", 15)), cols, new XElement(S + "sheetData", rows));
            if (filter && lastRow >= 5) root.Add(new XElement(S + "autoFilter", new XAttribute("ref", "A5:" + Column(columns) + lastRow)));
            return new XDocument(root);
        }

        private static XElement Row(int row, object[] values, int style = 0)
        {
            var element = new XElement(S + "row", new XAttribute("r", row));
            for (int i = 0; i < values.Length; i++) element.Add(Cell(Column(i + 1) + row, values[i], style));
            return element;
        }

        private static XElement Cell(string address, object value, int style)
        {
            bool number = value is int || value is double;
            var cell = new XElement(S + "c", new XAttribute("r", address), new XAttribute("s", style == 0 && value is double ? 2 : style));
            if (number) cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
            else
            {
                cell.SetAttributeValue("t", "inlineStr");
                string text = Clean(value == null ? "" : value.ToString());
                cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            }
            return cell;
        }

        private static string Clean(string text)
        {
            var buffer = new StringBuilder();
            for (int i = 0; i < text.Length && buffer.Length < 32767; i++)
            {
                char ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                { if (buffer.Length < 32766) { buffer.Append(ch); buffer.Append(text[++i]); } }
                else if (XmlConvert.IsXmlChar(ch)) buffer.Append(ch);
            }
            return buffer.ToString();
        }

        private static string Column(int number)
        {
            string name = "";
            while (number > 0) { number--; name = (char)('A' + number % 26) + name; number /= 26; }
            return name;
        }

        private static void FillTemplate(ZipArchive archive, string name, int start, BomResult result)
        {
            var workbook = Read(archive, "xl/workbook.xml");
            var sheets = workbook.Root.Element(S + "sheets");
            var sheet = string.IsNullOrWhiteSpace(name) ? sheets.Elements(S + "sheet").FirstOrDefault() :
                sheets.Elements(S + "sheet").FirstOrDefault(e => string.Equals((string)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
            if (sheet == null) throw new InvalidOperationException("Template sheet not found.");
            var relationships = Read(archive, "xl/_rels/workbook.xml.rels");
            var relationship = relationships.Root.Elements(P + "Relationship").FirstOrDefault(e => (string)e.Attribute("Id") == (string)sheet.Attribute(R + "id"));
            if (relationship == null || (string)relationship.Attribute("Type") != WorksheetType || (string)relationship.Attribute("TargetMode") == "External")
                throw new InvalidOperationException("Choose a normal worksheet in the template.");
            string path = new Uri(new Uri("http://package/xl/workbook.xml"), (string)relationship.Attribute("Target")).AbsolutePath.TrimStart('/');
            var document = Read(archive, path);
            var data = document.Root.Element(S + "sheetData");
            if (data == null || document.Root.Element(S + "sheetProtection") != null || document.Root.Element(S + "tableParts") != null)
                throw new InvalidOperationException("Template data sheet must be unprotected and must not contain Excel tables.");
            int end = start + result.Rows.Count - 1;
            var visibleColumns = result.Layout.Columns.Where(c => c.IsVisible).ToList();
            bool mapped = result.TemplateMapping.Count > 0;
            if (mapped && visibleColumns.Any(c => result.TemplateMapping.All(m => m.Field != c.Key)))
                throw new InvalidOperationException("Template mapping is missing fields: " + string.Join(", ", visibleColumns.Where(c => result.TemplateMapping.All(m => m.Field != c.Key)).Select(c => c.Header)) + ". Update Tools > Template column mapping.");
            var targetColumns = visibleColumns.Select((c, i) => mapped ? StudioState.ColumnNumber(result.TemplateMapping.FirstOrDefault(m => m.Field == c.Key)?.Column) : i + 1).ToArray();
            if (targetColumns.Distinct().Count() != targetColumns.Length) throw new InvalidOperationException("Template columns must be unique.");
            var outputColumns = new HashSet<int>(targetColumns);
            int columnCount = targetColumns.Max();
            if (result.TemplateTotalRow != 0 && (result.TemplateTotalRow <= end || result.TemplateTotalRow > 1048576)) throw new InvalidOperationException("Total row must be below all BOM data and within Excel row limits.");
            var actualRows = result.Rows.Where(r => !r.IsDecoration).ToList();
            if (result.TemplateTotalRow != 0 && actualRows.Select(r => r.Unit).Distinct().Count() > 1) throw new InvalidOperationException("A template total row requires a single unit. Use BOM grouping totals for mixed units.");
            string lastColumn = Column(columnCount);
            var merges = document.Root.Element(S + "mergeCells");
            if (merges != null)
                foreach (var merge in merges.Elements(S + "mergeCell"))
                {
                    var range = ((string)merge.Attribute("ref")).Split(':');
                    var a = Address(range[0]); var b = Address(range.Length > 1 ? range[1] : range[0]);
                    if (outputColumns.Any(col => col >= a.Item1 && col <= b.Item1) && ((a.Item2 <= end && b.Item2 >= Math.Max(1, start - 1)) || (result.TemplateTotalRow > 0 && a.Item2 <= result.TemplateTotalRow && b.Item2 >= result.TemplateTotalRow)))
                        throw new InvalidOperationException("Merged cells overlap the output/header area A" + Math.Max(1, start - 1) + ":" + lastColumn + end + ". Use an unmerged data area.");
                }
            var styles = new Dictionary<int, string>();
            var seed = data.Elements(S + "row").FirstOrDefault(r => (int?)r.Attribute("r") == start);
            var rowAttributes = seed == null ? new List<XAttribute>() : seed.Attributes().Where(a => a.Name != "r" && a.Name != "spans").Select(a => new XAttribute(a)).ToList();
            if (seed != null)
                foreach (var cell in seed.Elements(S + "c"))
                { int column = Address((string)cell.Attribute("r")).Item1; if (outputColumns.Contains(column)) styles[column] = (string)cell.Attribute("s"); }
            // A blank template avoids leftover data when a subsequent export has fewer rows.
            foreach (var row in data.Elements(S + "row").Where(r => (int?)r.Attribute("r") >= start))
                foreach (var cell in row.Elements(S + "c"))
                    if (outputColumns.Contains(Address((string)cell.Attribute("r")).Item1) &&
                        (cell.Element(S + "v") != null || cell.Element(S + "f") != null ||
                         cell.Descendants(S + "t").Any(t => !string.IsNullOrEmpty(t.Value))))
                        throw new InvalidOperationException("Template A:" + lastColumn + " from data row onward must be empty (styles are allowed). Move totals/formulas outside this area or use another sheet.");
            var rowsByNumber = data.Elements(S + "row").ToDictionary(r => (int)r.Attribute("r"));
            if (start > 1)
            {
                XElement header;
                if (!rowsByNumber.TryGetValue(start - 1, out header))
                { header = new XElement(S + "row", new XAttribute("r", start - 1)); data.Add(header); }
                var oldStyles = header.Elements(S + "c").ToDictionary(c => Address((string)c.Attribute("r")).Item1, c => (string)c.Attribute("s"));
                header.Elements(S + "c").Where(c => mapped ? outputColumns.Contains(Address((string)c.Attribute("r")).Item1) : Address((string)c.Attribute("r")).Item1 <= Math.Max(columnCount, BomLayout.Defaults().Count)).Remove();
                int columnIndex = 0;
                foreach (var item in result.Layout.Columns.Where(c => c.IsVisible))
                {
                    int col = targetColumns[columnIndex++];
                    var cell = Cell(Column(col) + (start - 1), item.Header, 0); string style;
                    if (oldStyles.TryGetValue(col, out style) && style != null) cell.SetAttributeValue("s", style); else cell.Attribute("s")?.Remove();
                    header.Add(cell);
                }
                var sorted = header.Elements(S + "c").OrderBy(c => Address((string)c.Attribute("r")).Item1).ToList();
                sorted.Remove(); header.Add(sorted); header.Attribute("spans")?.Remove();
            }
            int rowNumber = start;
            foreach (var bom in result.Rows)
            {
                XElement row;
                rowsByNumber.TryGetValue(rowNumber, out row);
                if (row == null) { row = new XElement(S + "row", new XAttribute("r", rowNumber), rowAttributes.Select(a => new XAttribute(a))); data.Add(row); }
                row.Elements(S + "c").Where(c => outputColumns.Contains(Address((string)c.Attribute("r")).Item1)).Remove();
                object[] values = result.Layout.Values(bom);
                for (int i = 0; i < values.Length; i++)
                {
                    var cell = Cell(Column(targetColumns[i]) + rowNumber, values[i], 0);
                    string style;
                    if (styles.TryGetValue(targetColumns[i], out style) && style != null) cell.SetAttributeValue("s", style);
                    else cell.Attribute("s").Remove();
                    row.Add(cell);
                }
                var orderedCells = row.Elements(S + "c").OrderBy(c => Address((string)c.Attribute("r")).Item1).ToList();
                orderedCells.Remove(); row.Add(orderedCells);
                row.Attribute("spans")?.Remove();
                rowNumber++;
            }
            if (result.TemplateTotalRow > 0) {
                XElement total;
                if (!rowsByNumber.TryGetValue(result.TemplateTotalRow, out total)) { total = new XElement(S + "row", new XAttribute("r", result.TemplateTotalRow)); data.Add(total); }
                total.Elements(S + "c").Where(c => outputColumns.Contains(Address((string)c.Attribute("r")).Item1)).Remove();
                var summary = new BomRow { Category = "Grand total", Quantity = actualRows.Sum(r => r.Quantity), ElementCount = actualRows.Sum(r => r.ElementCount), Unit = actualRows.FirstOrDefault()?.Unit, ExtraValues = new Dictionary<string, string> { { "__decoration", "total" } } };
                var values = result.Layout.Values(summary); for (int i = 0; i < values.Length; i++) total.Add(Cell(Column(targetColumns[i]) + result.TemplateTotalRow, values[i], 0));
                var orderedCells = total.Elements(S + "c").OrderBy(c => Address((string)c.Attribute("r")).Item1).ToList(); orderedCells.Remove(); total.Add(orderedCells);
            }
            var orderedRows = data.Elements(S + "row").OrderBy(r => (int)r.Attribute("r")).ToList();
            orderedRows.Remove(); data.Add(orderedRows);
            // Let Excel calculate used range; keep all existing formatting and print settings.
            document.Root.Element(S + "dimension")?.Remove();
            var autoFilter = document.Root.Element(S + "autoFilter");
            if (autoFilter != null && start > 1 && !mapped) autoFilter.SetAttributeValue("ref", "A" + (start - 1) + ":" + lastColumn + end);
            // Audit/notes sheets cannot assume the template's style index definitions.
            Write(archive, path, document);
            var calc = workbook.Root.Element(S + "calcPr");
            if (calc == null)
            {
                calc = new XElement(S + "calcPr");
                var later = new[] { "oleSize", "customWorkbookViews", "pivotCaches", "smartTagPr", "smartTagTypes", "webPublishing", "fileRecoveryPr", "webPublishObjects", "extLst" };
                var following = workbook.Root.Elements().FirstOrDefault(e => later.Contains(e.Name.LocalName));
                if (following == null) workbook.Root.Add(calc); else following.AddBeforeSelf(calc);
            }
            calc.SetAttributeValue("fullCalcOnLoad", 1);
            calc.SetAttributeValue("forceFullCalc", 1);
            calc.SetAttributeValue("calcMode", "auto");
            Write(archive, "xl/workbook.xml", workbook);
        }

        private static XDocument WithoutStyles(XDocument document)
        {
            foreach (var cell in document.Descendants(S + "c")) cell.Attribute("s")?.Remove();
            return document;
        }

        private static Tuple<int, int> Address(string reference)
        {
            int column = 0, i = 0;
            reference = reference.Replace("$", "").ToUpperInvariant();
            while (i < reference.Length && char.IsLetter(reference[i])) column = column * 26 + reference[i++] - 'A' + 1;
            return Tuple.Create(column, int.Parse(reference.Substring(i), CultureInfo.InvariantCulture));
        }

        private static XDocument Read(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path);
            if (entry == null) throw new InvalidOperationException("Invalid .xlsx package: missing " + path);
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader);
        }

        private static void Write(ZipArchive archive, string path, XDocument document)
        {
            // Create mode cannot query existing entries.
            if (archive.Mode == ZipArchiveMode.Update) archive.GetEntry(path)?.Delete();
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
                document.Save(writer);
        }
    }
}
