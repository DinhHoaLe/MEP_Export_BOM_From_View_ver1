using System.Collections.Generic;
using System.ComponentModel;

namespace MEP_Export_BOM_From_View_ver1
{
    public enum ExportScope { EntireModel, View3D, FloorPlan, ScopeBox }

    public sealed class ElementProperty
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string Access { get; set; }
    }
    public sealed class ImportMapping
    {
        public string FileHeading { get; set; }
        public string FieldKey { get; set; }
        public string ParameterName { get; set; }
    }
    public sealed class ImportedElementRow
    {
        public ElementRecord Record { get; set; }
        public int ElementId { get { return Record.ElementId; } }
        public string Source { get { return Record.SourceName; } }
        public string Category { get { return Record.Category; } }
        public string Family { get { return Record.Family; } }
        public string Type { get { return Record.Type; } }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public sealed class CategoryOption : INotifyPropertyChanged
    {
        private bool selected;
        public int Id { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
        public bool IsAvailable { get { return Count > 0; } }
        public string Availability { get { return IsAvailable ? Count + " elements in scope" : "Not present in this scope"; } }
        public bool IsSelected
        {
            get { return selected; }
            set { value = value && IsAvailable; if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class ElementRecord
    {
        public string SourceKey { get; set; } = "Host";
        public string SourceName { get; set; } = "Host";
        public string UniqueId { get; set; }
        public int LinkInstanceId { get; set; }
        public string Identity { get { return SourceKey + ":" + (UniqueId ?? ElementId.ToString()); } }
        public Dictionary<string, double> Measures { get; set; } = new Dictionary<string, double>();
        public string AutoUnit { get; set; }
        public int ElementId { get; set; }
        public int CategoryId { get; set; }
        public int TypeId { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string System { get; set; }
        public string Size { get; set; }
        public string Unit { get; set; }
        public double Quantity { get; set; }
        public string SizeKey { get; set; }
        public string Note { get; set; }
        public Dictionary<string, string> ExtraValues { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, int> GlobalParameters { get; set; } = new Dictionary<string, int>();
    }

    public sealed class BomRow
    {
        public string Source { get; set; }
        public string Warnings { get; set; }
        public bool HasWarnings { get { return !string.IsNullOrEmpty(Warnings); } }
        public List<string> RecordKeys { get; set; } = new List<string>();
        public bool IsDecoration { get { return ExtraValues.ContainsKey("__decoration"); } }
        public bool IsSummary { get { string kind; return ExtraValues.TryGetValue("__decoration", out kind) && kind != "blank"; } }
        public object DisplayNo { get { return IsDecoration ? (object)"" : No; } }
        public object DisplayQuantity { get { string kind; return ExtraValues.TryGetValue("__decoration", out kind) && kind != "total" ? (object)"" : Quantity; } }
        public object DisplayElementCount { get { return IsDecoration && ElementCount == 0 ? (object)"" : ElementCount; } }
        public int No { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string System { get; set; }
        public string Size { get; set; }
        public string Unit { get; set; }
        public double Quantity { get; set; }
        public int ElementCount { get; set; }
        public string ElementIds { get; set; }
        public Dictionary<string, string> ExtraValues { get; set; } = new Dictionary<string, string>();
    }

    public sealed class BomResult
    {
        public string Model { get; set; }
        public string Scope { get; set; }
        public BomLayout Layout { get; set; } = new BomLayout();
        public PreviewSettings Query { get; set; }
        public List<TemplateMapping> TemplateMapping { get; set; } = new List<TemplateMapping>();
        public int TemplateTotalRow { get; set; }
        public List<ParameterField> CommonParameters { get; set; } = new List<ParameterField>();
        public List<ParameterField> ParameterFields { get; set; } = new List<ParameterField>();
        public List<ElementRecord> Elements { get; set; } = new List<ElementRecord>();
        public List<BomRow> Rows { get; set; } = new List<BomRow>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
