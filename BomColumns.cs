using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MEP_Export_BOM_From_View_ver1
{
    public sealed class ParameterField
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class BomColumn : INotifyPropertyChanged
    {
        private string header;
        private bool visible = true;
        public string Key { get; set; }
        public string Header { get { return header; } set { header = value; Changed(nameof(Header)); } }
        public bool IsRequired { get { return BomLayout.RequiredKeys.Contains(Key); } }
        public bool CanHide { get { return !IsRequired; } }
        public bool IsVisible { get { return visible || IsRequired; } set { visible = value || IsRequired; Changed(nameof(IsVisible)); } }
        public string Requirement { get { return IsRequired ? "Required" : "Optional"; } }
        public BomColumn Clone() { return new BomColumn { Key = Key, Header = Header, IsVisible = IsVisible }; }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    public sealed class BomLayout
    {
        public static readonly HashSet<string> RequiredKeys = new HashSet<string> { "Category", "Family", "Type", "Unit", "Quantity", "ElementCount" };
        public string Title { get; set; } = "MEP BILL OF MATERIALS";
        public List<BomColumn> Columns { get; set; } = Defaults();
        public BomLayout Clone() { return new BomLayout { Title = Title, Columns = Columns.Select(c => c.Clone()).ToList() }; }
        public static List<BomColumn> Defaults()
        {
            string[] keys = { "No", "Category", "Family", "Type", "System", "Size", "Unit", "Quantity", "ElementCount", "ElementIds" };
            string[] labels = { "STT", "Category", "Family", "Type", "System", "Size", "Unit", "Quantity", "Element Count", "Element IDs" };
            return keys.Select((key, i) => new BomColumn { Key = key, Header = labels[i] }).ToList();
        }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Title)) throw new InvalidOperationException("BOM title cannot be blank.");
            if (Columns == null || RequiredKeys.Any(key => !Columns.Any(c => c.Key == key && c.IsVisible)))
                throw new InvalidOperationException("Required columns cannot be removed or hidden.");
            if (Columns.Any(c => string.IsNullOrWhiteSpace(c.Header)) || Columns.Select(c => c.Key).Distinct().Count() != Columns.Count)
                throw new InvalidOperationException("Column headings must be nonblank and fields must not be duplicated.");
            if (Columns.Where(c => c.IsVisible).Select(c => c.Header.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Columns.Count(c => c.IsVisible))
                throw new InvalidOperationException("Visible column headings must be unique.");
        }
        public object[] Values(BomRow row)
        { return Columns.Where(c => c.IsVisible).Select(c => Value(row, c.Key)).ToArray(); }
        public static object Value(BomRow row, string key)
        {
            switch (key)
            {
                case "No": return row.No; case "Category": return row.Category; case "Family": return row.Family;
                case "Type": return row.Type; case "System": return row.System; case "Size": return row.Size;
                case "Unit": return row.Unit; case "Quantity": return row.Quantity;
                case "ElementCount": return row.ElementCount; case "ElementIds": return row.ElementIds;
                default: string value; return row.ExtraValues.TryGetValue(key, out value) ? value : "";
            }
        }
    }

    public sealed class ScheduleRequest
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string ScheduleName { get; set; }
        public bool Supported { get; set; } = true;
        public string Status { get { return Supported ? "Ready" : "Category does not support a regular schedule"; } }
    }

    public static class ScheduleNames
    {
        public static void Validate(IEnumerable<ScheduleRequest> requests, IEnumerable<string> existingNames)
        {
            var used = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var items = requests.Where(r => r.Supported).ToList();
            if (items.Count == 0) throw new InvalidOperationException("No selected category supports a regular schedule.");
            foreach (var item in items)
            {
                string name = (item.ScheduleName ?? "").Trim();
                if (name.Length == 0) throw new InvalidOperationException("Enter a schedule name for " + item.CategoryName + ".");
                if (name.IndexOfAny("\\:{}[]|;<>?`~".ToCharArray()) >= 0)
                    throw new InvalidOperationException("Schedule name contains an invalid character: " + name);
                if (!used.Add(name)) throw new InvalidOperationException("Schedule name already exists or is duplicated: " + name);
            }
        }
        public static string Suggest(string category, ISet<string> used)
        {
            string baseName = "BOM - " + category + " - Entire Model";
            foreach (char ch in "\\:{}[]|;<>?`~") baseName = baseName.Replace(ch, '-');
            string name = baseName; int suffix = 2;
            while (!used.Add(name)) name = baseName + " (" + suffix++ + ")";
            return name;
        }
    }
}
