using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Security.Cryptography;

namespace MEP_Export_BOM_From_View_ver1
{
    public sealed class ImportRow
    {
        public int Number { get; set; }
        public List<string> Cells { get; set; } = new List<string>();
    }
    public sealed class ImportSheet
    {
        public string Name { get; set; }
        public List<ImportRow> Rows { get; set; } = new List<ImportRow>();
        public override string ToString() { return Name; }
    }
    public sealed class ImportRowMatch
    {
        public int Row { get; set; }
        public string Value { get; set; }
        public int Matches { get; set; }
        public string Status { get { return Matches > 0 ? "Matched" : "No match"; } }
    }
    public sealed class ImportMatchResult
    {
        public List<ImportMapping> Mappings { get; set; } = new List<ImportMapping>();
        public HashSet<string> Identities { get; set; } = new HashSet<string>();
        public List<ImportRowMatch> Rows { get; set; } = new List<ImportRowMatch>();
    }
    public sealed class ImportField
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool IsNumeric { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public static class ImportSelection
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const int MaxRows = 20000;

        public static List<ImportSheet> Read(string path)
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidOperationException("Import file exceeds 20 MB.");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".csv")
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                    return new List<ImportSheet> { ReadCsv(reader.ReadToEnd(), Path.GetFileName(path)) };
            }
            if (extension != ".xlsx") throw new InvalidOperationException("Import supports .xlsx or UTF-8 CSV files.");
            using (var archive = ZipFile.OpenRead(path))
            {
                var workbook = Xml(archive, "xl/workbook.xml"); var rels = Xml(archive, "xl/_rels/workbook.xml.rels");
                var strings = archive.GetEntry("xl/sharedStrings.xml") == null ? new List<string>() :
                    Xml(archive, "xl/sharedStrings.xml").Descendants(S + "si").Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value))).ToList();
                var formats = new List<string>();
                if (archive.GetEntry("xl/styles.xml") != null)
                {
                    var styles = Xml(archive, "xl/styles.xml");
                    var customFormats = styles.Descendants(S + "numFmt").ToDictionary(f => (int)f.Attribute("numFmtId"), f => (string)f.Attribute("formatCode"));
                    foreach (var xf in styles.Root.Element(S + "cellXfs")?.Elements(S + "xf") ?? Enumerable.Empty<XElement>())
                    { string format; formats.Add(customFormats.TryGetValue((int?)xf.Attribute("numFmtId") ?? 0, out format) ? format : ""); }
                }
                var sheets = new List<ImportSheet>();
                foreach (var sheet in workbook.Descendants(S + "sheet"))
                {
                    var rel = rels.Root.Elements(P + "Relationship").FirstOrDefault(e => (string)e.Attribute("Id") == (string)sheet.Attribute(R + "id"));
                    if (rel == null || (string)rel.Attribute("TargetMode") == "External" || !((string)rel.Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal)) continue;
                    string part = new Uri(new Uri("http://package/xl/workbook.xml"), (string)rel.Attribute("Target")).AbsolutePath.TrimStart('/');
                    var result = new ImportSheet { Name = (string)sheet.Attribute("name") };
                    foreach (var row in Xml(archive, part).Descendants(S + "sheetData").Elements(S + "row"))
                    {
                        if (result.Rows.Count >= MaxRows) throw new InvalidOperationException("Import supports at most 20,000 rows per sheet.");
                        var item = new ImportRow { Number = (int?)row.Attribute("r") ?? result.Rows.Count + 1 };
                        foreach (var cell in row.Elements(S + "c"))
                        {
                            int column = Column((string)cell.Attribute("r"));
                            if (column > 1024) throw new InvalidOperationException("Import supports at most 1,024 columns.");
                            while (item.Cells.Count < column) item.Cells.Add("");
                            string type = (string)cell.Attribute("t"), value = (string)cell.Element(S + "v") ?? "";
                            if (type == "s") { int index; if (!int.TryParse(value, out index) || index < 0 || index >= strings.Count) throw new InvalidOperationException("Invalid shared string in Excel."); value = strings[index]; }
                            else if (type == "inlineStr") value = string.Concat(cell.Descendants(S + "t").Select(t => t.Value));
                            else if (type == "b") value = value == "1" ? "TRUE" : "FALSE";
                            else if (type == "e") throw new InvalidOperationException("Excel contains an error at " + sheet.Attribute("name")?.Value + " / " + cell.Attribute("r")?.Value);
                            else if (type == null || type == "n")
                            {
                                int style = (int?)cell.Attribute("s") ?? -1; double number;
                                if (style >= 0 && style < formats.Count && Regex.IsMatch(formats[style], "^0{2,}$") &&
                                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                                    value = number.ToString(formats[style], CultureInfo.InvariantCulture);
                            }
                            if (cell.Element(S + "f") != null && cell.Element(S + "v") == null)
                                throw new InvalidOperationException("Formula has no saved result at " + cell.Attribute("r")?.Value + ". Recalculate and save the workbook in Excel before import.");
                            item.Cells[column - 1] = value;
                        }
                        result.Rows.Add(item);
                    }
                    sheets.Add(result);
                }
                if (sheets.Count == 0) throw new InvalidOperationException("No worksheets found in this workbook.");
                return sheets;
            }
        }

        public static ImportSheet ReadCsv(string text, string name = "CSV")
        {
            text = text.TrimStart('\uFEFF');
            var counts = new Dictionary<char, int> { { ',', 0 }, { ';', 0 }, { '\t', 0 } }; bool quote = false;
            for (int i = 0; i < text.Length && text[i] != '\r' && text[i] != '\n'; i++)
            { if (text[i] == '"') quote = !quote; else if (!quote && counts.ContainsKey(text[i])) counts[text[i]]++; }
            char delimiter = counts.OrderByDescending(p => p.Value).First().Key;
            var result = new ImportSheet { Name = name }; var cells = new List<string>(); var value = new StringBuilder(); bool quoted = false, closed = false;
            Action finishCell = () => { cells.Add(value.ToString()); value.Clear(); closed = false; if (cells.Count > 1024) throw new InvalidOperationException("Too many CSV columns."); };
            Action finishRow = () => { finishCell(); result.Rows.Add(new ImportRow { Number = result.Rows.Count + 1, Cells = cells }); cells = new List<string>(); if (result.Rows.Count > MaxRows) throw new InvalidOperationException("Import supports at most 20,000 rows."); };
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; } else { quoted = false; closed = true; } }
                    else value.Append(ch);
                }
                else if (ch == delimiter) finishCell();
                else if (ch == '\r' || ch == '\n') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; finishRow(); }
                else if (ch == '"') { if (value.Length > 0 || closed) throw new InvalidOperationException("Invalid CSV quoting."); quoted = true; }
                else if (closed) { if (!char.IsWhiteSpace(ch)) throw new InvalidOperationException("Unexpected text after CSV closing quote."); }
                else value.Append(ch);
            }
            if (quoted) throw new InvalidOperationException("CSV has an unclosed quoted field.");
            if (value.Length > 0 || cells.Count > 0 || closed) finishRow();
            return result;
        }

        public static List<ImportField> Fields(BomLayout layout, IEnumerable<ParameterField> parameters)
        {
            var fields = new List<ImportField>();
            foreach (var column in BomLayout.Defaults().Where(c => c.Key != "No" && c.Key != "ElementCount"))
            {
                string key = column.Key == "ElementIds" ? "ElementId" : column.Key;
                var field = new ImportField { Key = key, Name = column.Header, IsNumeric = key == "Quantity", Aliases = new List<string> { column.Header, column.Key } };
                field.Aliases.AddRange(layout.Columns.Where(c => c.Key == column.Key).Select(c => c.Header)); fields.Add(field);
            }
            foreach (var field in new[] { new ImportField { Key = "UniqueId", Name = "Unique ID" }, new ImportField { Key = "Source", Name = "Source" },
                new ImportField { Key = "LinkInstanceId", Name = "Link Instance ID" }, new ImportField { Key = "FamilyAndType", Name = "Family and Type" } })
            { field.Aliases.AddRange(new[] { field.Name, field.Key }); fields.Add(field); }
            fields.Single(f => f.Key == "ElementId").Aliases.Add("Element ID");
            foreach (var parameter in parameters) fields.Add(new ImportField { Key = parameter.Key, Name = parameter.Name, IsNumeric = parameter.IsNumeric,
                Aliases = new List<string> { parameter.Name, parameter.Key }.Concat(layout.Columns.Where(c => c.Key == parameter.Key).Select(c => c.Header)).ToList() });
            // Distinguish parameters that have the same display name.
            foreach (var group in fields.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var field in group) field.Name += " [" + field.Key + "]";
            return fields;
        }

        public static ImportField Resolve(string heading, IList<ImportField> fields)
        {
            string normalized = Normalize(heading);
            var matches = fields.Where(f => f.Aliases.Any(a => Normalize(a) == normalized)).ToList();
            // A BOM heading is unambiguous even if a same-named raw parameter exists.
            var builtin = matches.Where(f => !f.Key.StartsWith("param:", StringComparison.Ordinal)).ToList();
            return builtin.Count == 1 ? builtin[0] : matches.Count == 1 ? matches[0] : null;
        }

        public static int GuessHeader(ImportSheet sheet, IList<ImportField> fields)
        { return sheet.Rows.Take(50).OrderByDescending(r => r.Cells.Count(c => Resolve(c, fields) != null)).ThenBy(r => r.Number).FirstOrDefault()?.Number ?? 1; }

        public static string Fingerprint(IEnumerable<string> identities)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", identities.OrderBy(id => id, StringComparer.Ordinal))))).Replace("-", "");
        }

        public static ImportMatchResult Match(ImportSheet sheet, int headerRow, int column, ImportField field, IList<ElementRecord> elements)
        {
            if (field == null || column < 0) throw new InvalidOperationException("Choose a key column and target parameter/title.");
            if (!sheet.Rows.Any(r => r.Number == headerRow)) throw new InvalidOperationException("Header row was not found.");
            var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var element in elements)
                foreach (string key in Tokens(Value(element, field.Key), field.IsNumeric, false))
                { HashSet<string> ids; if (!index.TryGetValue(key, out ids)) index[key] = ids = new HashSet<string>(); ids.Add(element.Identity); }
            var result = new ImportMatchResult();
            foreach (var row in sheet.Rows.Where(r => r.Number > headerRow))
            {
                string value = column < row.Cells.Count ? row.Cells[column].Trim() : "";
                if (string.IsNullOrEmpty(value)) continue;
                var ids = new HashSet<string>();
                var values = field.Key == "ElementId" ? value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) : new[] { value };
                foreach (var item in values)
                    foreach (var key in Tokens(item, field.IsNumeric, true)) { HashSet<string> matches; if (index.TryGetValue(key, out matches)) ids.UnionWith(matches); }
                result.Identities.UnionWith(ids); result.Rows.Add(new ImportRowMatch { Row = row.Number, Value = value, Matches = ids.Count });
            }
            if (result.Rows.Count == 0) throw new InvalidOperationException("The key column has no nonblank data below the header.");
            return result;
        }

        public static string ElementValue(ElementRecord record, string key) { return Value(record, key); }
        public static List<ImportMapping> Mappings(ImportSheet sheet, int headerRow, IList<ImportField> fields, int keyColumn, ImportField target)
        {
            var header = sheet.Rows.First(r => r.Number == headerRow);
            return header.Cells.Select((heading, i) => new { Heading = heading, Field = i == keyColumn ? target : Resolve(heading, fields) }).Where(item => item.Field != null)
                .Select(item => new ImportMapping { FileHeading = item.Heading, FieldKey = item.Field.Key, ParameterName = item.Field.Name }).ToList();
        }
        public static List<ImportedElementRow> ElementRows(IEnumerable<ElementRecord> records, IEnumerable<ImportMapping> mappings)
        {
            var keys = mappings.Select(m => m.FieldKey).Distinct().ToList();
            return records.Select(record => new ImportedElementRow { Record = record, Values = keys.ToDictionary(key => key, key => Value(record, key)) }).ToList();
        }

        private static string Value(ElementRecord record, string key)
        {
            switch (key)
            {
                case "ElementId": return record.ElementId.ToString(CultureInfo.InvariantCulture); case "UniqueId": return record.UniqueId ?? "";
                case "LinkInstanceId": return record.LinkInstanceId.ToString(CultureInfo.InvariantCulture); case "Source": return record.SourceName ?? "";
                case "FamilyAndType": return record.Family + ": " + record.Type;
                case "Category": return record.Category ?? ""; case "Family": return record.Family ?? ""; case "Type": return record.Type ?? "";
                case "System": return record.System ?? ""; case "Size": return record.Size ?? ""; case "Unit": return record.Unit ?? "";
                case "Quantity": return record.Quantity.ToString("R", CultureInfo.InvariantCulture) + (record.Unit == "pcs" ? "" : " " + record.Unit);
                default: string value; return record.ExtraValues.TryGetValue(key, out value) ? value : "";
            }
        }

        private static string Normalize(string value) { return Regex.Replace((value ?? "").Trim(), @"\s+", " ").ToUpperInvariant(); }
        private static IEnumerable<string> Tokens(string value, bool numeric, bool input)
        {
            string text = Normalize(value);
            if (string.IsNullOrEmpty(text)) yield break;
            if (!numeric) { yield return "T:" + text; yield break; }
            if (text == "TRUE" || text == "YES") text = "1"; else if (text == "FALSE" || text == "NO") text = "0";
            var match = Regex.Match(text, @"^([+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:E[+-]?\d+)?)\s*([A-Z²³%0-9]*)$");
            double number;
            if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            { yield return "T:" + text; yield break; }
            string unit = match.Groups[2].Value;
            double factor = 1; string dimension = "";
            switch (unit)
            {
                case "MM": factor = .001; dimension = "L"; break; case "CM": factor = .01; dimension = "L"; break;
                case "M": dimension = "L"; break; case "FT": factor = .3048; dimension = "L"; break; case "IN": factor = .0254; dimension = "L"; break;
                case "M²": case "M2": dimension = "A"; break; case "M³": case "M3": dimension = "V"; break;
                case "": break;
                default: yield return "T:" + text; yield break;
            }
            if (unit == "" || !input) yield return "N:" + number.ToString("G12", CultureInfo.InvariantCulture);
            if (dimension != "") yield return dimension + ":" + (number * factor).ToString("G12", CultureInfo.InvariantCulture);
        }

        private static int Column(string address)
        { int value = 0; foreach (char ch in (address ?? "").ToUpperInvariant()) { if (ch < 'A' || ch > 'Z') break; value = value * 26 + ch - 'A' + 1; } if (value == 0) throw new InvalidOperationException("Invalid Excel cell address."); return value; }
        private static XDocument Xml(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path); if (entry == null) throw new InvalidOperationException("Invalid .xlsx file: missing " + path);
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 })) return XDocument.Load(reader);
        }
    }
}
