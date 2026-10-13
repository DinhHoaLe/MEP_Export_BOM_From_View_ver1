using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MEP_Export_BOM_From_View_ver1
{
    public sealed class PreviewFilter
    {
        public string Field { get; set; }
        public string Operator { get; set; } = "Equal";
        public string Value { get; set; } = "";
        public PreviewFilter Clone() { return (PreviewFilter)MemberwiseClone(); }
    }
    public sealed class PreviewSort
    {
        public string Field { get; set; }
        public bool Descending { get; set; }
        public bool Header { get; set; }
        public bool Footer { get; set; }
        public bool FooterTitle { get; set; } = true;
        public bool FooterCount { get; set; } = true;
        public bool BlankLine { get; set; }
        public PreviewSort Clone() { return (PreviewSort)MemberwiseClone(); }
    }
    public sealed class PreviewSettings
    {
        public List<PreviewFilter> Filters { get; set; } = new List<PreviewFilter>();
        public List<PreviewSort> Sorts { get; set; } = new List<PreviewSort>();
        public bool Itemized { get; set; }
        public bool GrandTotal { get; set; }
        public bool GrandTotalTitle { get; set; } = true;
        public bool GrandTotalCount { get; set; } = true;
        public string TotalTitle { get; set; } = "Grand total";
        public PreviewSettings Clone() { return new PreviewSettings { Filters = Filters.Select(f => f.Clone()).ToList(), Sorts = Sorts.Select(s => s.Clone()).ToList(), Itemized = Itemized,
            GrandTotal = GrandTotal, GrandTotalTitle = GrandTotalTitle, GrandTotalCount = GrandTotalCount, TotalTitle = TotalTitle }; }
        public static readonly string[] Operators = { "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Contains", "NotContains", "BeginsWith", "NotBeginsWith", "EndsWith", "NotEndsWith", "HasValue", "HasNoValue", "HasParameter", "IsAssociatedWithGlobalParameter", "IsNotAssociatedWithGlobalParameter" };
        public void Validate(IEnumerable<string> fields, bool native)
        {
            var keys = new HashSet<string>(fields);
            if (Filters.Count > 8 || Sorts.Count > 4) throw new InvalidOperationException("Use at most 8 filters and 4 sort/group fields (Revit limits).");
            if (Filters.Any(f => !keys.Contains(f.Field ?? "") || !Operators.Contains(f.Operator)) || Sorts.Any(s => !keys.Contains(s.Field ?? "")))
                throw new InvalidOperationException("Choose a valid field and operator for every rule.");
            if (Sorts.Select(s => s.Field).Distinct().Count() != Sorts.Count) throw new InvalidOperationException("Sort/group fields must be unique.");
            foreach (var filter in Filters.Where(f => f.Operator.Contains("GlobalParameter"))) {
                int id; if (!int.TryParse(filter.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0) throw new InvalidOperationException("Enter a positive global parameter ID.");
                if (!native && !filter.Field.StartsWith("param:", StringComparison.Ordinal)) throw new InvalidOperationException("Global parameter association requires a Revit parameter field.");
            }
            if (!native) foreach (var filter in Filters.Where(f => new[] { "Quantity", "ElementCount", "No" }.Contains(f.Field) && new[] { "Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual" }.Contains(f.Operator)))
            { double value; if (!double.TryParse(filter.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new InvalidOperationException("Enter a numeric value with a decimal point for " + filter.Field); }
        }
    }
    public sealed class NativeField
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Heading { get; set; }
        public bool Visible { get; set; } = true;
        public bool Total { get; set; }
        public bool CanTotal { get; set; }
        public NativeField Clone() { return (NativeField)MemberwiseClone(); }
    }
    public sealed class NativeScheduleOptions
    {
        public string Title { get; set; }
        public List<int> ScopeElementIds { get; set; }
        public List<NativeField> Fields { get; set; } = new List<NativeField>();
        public List<NativeField> Available { get; set; } = new List<NativeField>();
        public PreviewSettings Settings { get; set; } = new PreviewSettings { Itemized = true, GrandTotal = true };
        public NativeScheduleOptions Clone() { return new NativeScheduleOptions { Title = Title, ScopeElementIds = ScopeElementIds == null ? null : new List<int>(ScopeElementIds), Fields = Fields.Select(f => f.Clone()).ToList(), Available = Available.Select(f => f.Clone()).ToList(), Settings = Settings.Clone() }; }
    }
    public static class BomQuery
    {
        public static List<BomRow> Apply(IEnumerable<ElementRecord> source, PreviewSettings settings)
        {
            var elements = source.Where(e => settings.Filters.Where(f => f.Operator.Contains("GlobalParameter")).All(f => {
                int id; bool associated = e.GlobalParameters.TryGetValue(f.Field, out id) && id == int.Parse(f.Value, CultureInfo.InvariantCulture);
                return f.Operator == "IsAssociatedWithGlobalParameter" ? associated : !associated;
            })).ToList();
            var rows = settings.Itemized ? elements.Select(e => BomAggregator.Aggregate(new[] { e })[0]).ToList() : BomAggregator.Aggregate(elements);
            rows = rows.Where(r => settings.Filters.Where(f => !f.Operator.Contains("GlobalParameter")).All(f => Match(BomLayout.Value(r, f.Field), f))).ToList();
            if (!settings.Itemized && settings.Sorts.Count > 0)
            {
                // Group by the chosen sort fields; unit remains a boundary to avoid mixed-unit totals.
                rows = rows.GroupBy(r => string.Join("", settings.Sorts.Select(s => { string value = Convert.ToString(BomLayout.Value(r, s.Field), CultureInfo.InvariantCulture) ?? ""; return value.Length + ":" + value; })) + "|" + r.Unit)
                    .Select(group => {
                        var members = group.ToList(); var first = members[0];
                        Func<Func<BomRow, string>, string> common = getter => members.Select(getter).Distinct().Count() == 1 ? getter(first) : "<varies>";
                        return new BomRow { Category = common(r => r.Category), Family = common(r => r.Family), Type = common(r => r.Type), System = common(r => r.System), Size = common(r => r.Size), Unit = first.Unit,
                            Source = common(r => r.Source), RecordKeys = members.SelectMany(r => r.RecordKeys).ToList(),
                            Quantity = members.Sum(r => r.Quantity), ElementCount = members.Sum(r => r.ElementCount), ElementIds = string.Join(", ", members.SelectMany(r => (r.ElementIds ?? "").Split(',')).Select(s => s.Trim()).Where(s => s.Length > 0)),
                            ExtraValues = members.SelectMany(r => r.ExtraValues.Keys).Distinct().ToDictionary(key => key, key => common(r => r.ExtraValues.ContainsKey(key) ? r.ExtraValues[key] : "")) };
                    }).ToList();
            }
            if (settings.Sorts.Count > 0) rows = rows.OrderBy(r => r, Comparer<BomRow>.Create((a, b) => { foreach (var sort in settings.Sorts) { int value = Compare(BomLayout.Value(a, sort.Field), BomLayout.Value(b, sort.Field)); if (value != 0) return sort.Descending ? -value : value; } return 0; })).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].No = i + 1;
            if (settings.Sorts.Count == 0 && !settings.GrandTotal) return rows;
            var output = new List<BomRow>();
            AddGroups(output, rows, settings.Sorts, 0);
            if (settings.GrandTotal) AddTotals(output, rows, settings.GrandTotalTitle ? settings.TotalTitle : "", settings.GrandTotalCount);
            return output;
        }
        private static void AddGroups(List<BomRow> output, List<BomRow> rows, List<PreviewSort> sorts, int depth)
        {
            if (depth == sorts.Count) { output.AddRange(rows); return; }
            var sort = sorts[depth];
            foreach (var group in rows.GroupBy(r => Convert.ToString(BomLayout.Value(r, sort.Field), CultureInfo.InvariantCulture)))
            {
                var members = group.ToList();
                if (sort.Header) output.Add(new BomRow { Category = group.Key, ExtraValues = new Dictionary<string, string> { { "__decoration", "header" } } });
                AddGroups(output, members, sorts, depth + 1);
                if (sort.Footer) AddTotals(output, members, sort.FooterTitle ? group.Key : "", sort.FooterCount);
                if (sort.BlankLine) output.Add(new BomRow { ExtraValues = new Dictionary<string, string> { { "__decoration", "blank" } } });
            }
        }
        private static void AddTotals(List<BomRow> output, List<BomRow> rows, string title, bool count)
        {
            foreach (var group in rows.GroupBy(r => r.Unit)) output.Add(new BomRow { Category = title, Unit = group.Key, Quantity = group.Sum(r => r.Quantity), ElementCount = count ? group.Sum(r => r.ElementCount) : 0,
                ExtraValues = new Dictionary<string, string> { { "__decoration", "total" } } });
        }
        public static int Compare(object a, object b)
        {
            if (a is double || a is int || a is long) { double number; if (double.TryParse(Convert.ToString(b, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return Convert.ToDouble(a).CompareTo(number); }
            return StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(a), Convert.ToString(b));
        }
        public static bool Match(object value, PreviewFilter filter)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; var expected = filter.Value ?? "";
            var comparison = Compare(value, expected); var comparisonType = StringComparison.CurrentCultureIgnoreCase;
            switch (filter.Operator)
            {
                case "Equal": return comparison == 0; case "NotEqual": return comparison != 0;
                case "GreaterThan": return comparison > 0; case "GreaterThanOrEqual": return comparison >= 0;
                case "LessThan": return comparison < 0; case "LessThanOrEqual": return comparison <= 0;
                case "Contains": return text.IndexOf(expected, comparisonType) >= 0; case "NotContains": return text.IndexOf(expected, comparisonType) < 0;
                case "BeginsWith": return text.StartsWith(expected, comparisonType); case "NotBeginsWith": return !text.StartsWith(expected, comparisonType);
                case "EndsWith": return text.EndsWith(expected, comparisonType); case "NotEndsWith": return !text.EndsWith(expected, comparisonType);
                case "HasValue": return text.Length > 0; case "HasNoValue": return text.Length == 0; case "HasParameter": return value != null;
                default: throw new InvalidOperationException("Unsupported BOM filter: " + filter.Operator);
            }
        }
    }
}
