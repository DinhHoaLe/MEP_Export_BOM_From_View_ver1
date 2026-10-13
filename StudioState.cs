using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;

namespace MEP_Export_BOM_From_View_ver1
{
    public sealed class MeasurementRule
    {
        public int CategoryId { get; set; }
        public string Category { get; set; }
        public string Method { get; set; } = "Auto";
    }
    public sealed class TemplateMapping
    {
        public string Field { get; set; }
        public string Heading { get; set; }
        public string Column { get; set; }
    }
    public sealed class StudioPreset
    {
        public int Version { get; set; } = 1;
        public BomLayout Layout { get; set; } = new BomLayout();
        public PreviewSettings Query { get; set; } = new PreviewSettings();
        public Dictionary<int, NativeScheduleOptions> Schedules { get; set; } = new Dictionary<int, NativeScheduleOptions>();
        public List<int> SelectedCategories { get; set; } = new List<int>();
        public List<MeasurementRule> Measurements { get; set; } = new List<MeasurementRule>();
        public List<TemplateMapping> Mapping { get; set; } = new List<TemplateMapping>();
        public string Template { get; set; }
        public string Sheet { get; set; }
        public int DataRow { get; set; } = 6;
        public int TotalRow { get; set; }
        public bool IncludeLinks { get; set; }
        public bool MatchScope { get; set; } = true;
        public bool CategorySheets { get; set; } = true;
    }
    public sealed class BomSnapshot
    {
        public int Version { get; set; } = 1;
        public string ModelKey { get; set; }
        public string Scope { get; set; }
        public string ExportedUtc { get; set; }
        public string SelectionKey { get; set; }
        public List<ElementRecord> Elements { get; set; } = new List<ElementRecord>();
    }
    public sealed class BomChange
    {
        public string Change { get; set; }
        public string Source { get; set; }
        public string Element { get; set; }
        public string Category { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public string Unit { get; set; }
        public double Delta { get; set; }
    }
    public static class StudioState
    {
        public static void Save<T>(string path, T value)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), ".studio-" + Guid.NewGuid().ToString("N") + ".xml");
            try {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true })) new DataContractSerializer(typeof(T)).WriteObject(writer, value);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static T Load<T>(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64000000 }))
                return (T)new DataContractSerializer(typeof(T)).ReadObject(reader);
        }
        public static List<BomChange> Compare(BomSnapshot before, BomSnapshot after)
        {
            if (before.Version != 1 || after.Version != 1) throw new InvalidOperationException("Unsupported snapshot version.");
            if (!string.Equals(before.ModelKey, after.ModelKey, StringComparison.Ordinal)) throw new InvalidOperationException("Snapshots belong to different host models.");
            if (before.Scope != after.Scope) throw new InvalidOperationException("Choose a snapshot from the same scope/view.");
            if (before.SelectionKey != after.SelectionKey) throw new InvalidOperationException("Category, filter, link or measurement settings differ. Use matching settings before comparing snapshots.");
            var old = before.Elements.ToDictionary(e => e.Identity); var current = after.Elements.ToDictionary(e => e.Identity);
            var changes = new List<BomChange>();
            foreach (var key in old.Keys.Union(current.Keys)) {
                ElementRecord a, b; old.TryGetValue(key, out a); current.TryGetValue(key, out b);
                if (a != null && b != null && a.Unit == b.Unit && Math.Abs(a.Quantity - b.Quantity) < 1e-8 && a.Type == b.Type && a.Family == b.Family && a.System == b.System && a.SizeKey == b.SizeKey && a.Category == b.Category) continue;
                var record = b ?? a;
                changes.Add(new BomChange { Change = a == null ? "Added" : b == null ? "Removed" : "Changed", Source = record.SourceName, Element = record.ElementId.ToString(), Category = record.Category,
                    Before = a == null ? "" : Describe(a), After = b == null ? "" : Describe(b),
                    Unit = a != null && b != null && a.Unit != b.Unit ? "Unit changed" : record.Unit,
                    Delta = a != null && b != null && a.Unit != b.Unit ? 0 : (b?.Quantity ?? 0) - (a?.Quantity ?? 0) });
            }
            return changes.OrderBy(c => c.Category).ThenBy(c => c.Element).ToList();
        }
        private static string Describe(ElementRecord e) { return e.Type + " · " + e.System + " · " + e.Size + " · " + e.Quantity.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + " " + e.Unit; }
        public static List<string> Quality(IEnumerable<ElementRecord> elements)
        {
            var warnings = new List<string>();
            foreach (var element in elements) {
                var issues = new List<string>();
                if (double.IsNaN(element.Quantity) || double.IsInfinity(element.Quantity) || element.Quantity <= 0) issues.Add("quantity is zero, negative or invalid");
                if (element.Category != null && (element.Category.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 || element.Category.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0)) {
                    if (string.IsNullOrWhiteSpace(element.System)) issues.Add("missing System"); if (string.IsNullOrWhiteSpace(element.Size)) issues.Add("missing Size");
                }
                if (issues.Count > 0) warnings.Add(element.SourceName + " / " + element.ElementId + ": " + string.Join("; ", issues));
            }
            return warnings;
        }
        public static int ColumnNumber(string column)
        {
            int number = 0; foreach (char ch in (column ?? "").Trim().ToUpperInvariant()) { if (ch < 'A' || ch > 'Z') throw new InvalidOperationException("Enter an Excel column such as A, B or AA."); number = checked(number * 26 + ch - 'A' + 1); if (number > 16384) throw new InvalidOperationException("Column exceeds XFD."); }
            if (number < 1) throw new InvalidOperationException("Column cannot be blank."); return number;
        }
    }
}
