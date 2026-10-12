using System.Collections.Generic;
using System.ComponentModel;

namespace MEP_Export_BOM_From_View_ver1
{
    public enum ExportScope { EntireModel, View3D, FloorPlan, ScopeBox }

    public sealed class CategoryOption : INotifyPropertyChanged
    {
        private bool selected;
        public int Id { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
        public bool IsSelected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class ElementRecord
    {
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
    }

    public sealed class BomRow
    {
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
        public List<ParameterField> CommonParameters { get; set; } = new List<ParameterField>();
        public List<ElementRecord> Elements { get; set; } = new List<ElementRecord>();
        public List<BomRow> Rows { get; set; } = new List<BomRow>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
