using System.Collections.Generic;
using System.Linq;

namespace MEP_Export_BOM_From_View_ver1
{
    public static class BomAggregator
    {
        public static List<BomRow> Aggregate(IEnumerable<ElementRecord> records)
        {
            var rows = records.GroupBy(e => new { e.SourceKey, e.CategoryId, e.TypeId, e.Family, e.Type, e.System, e.SizeKey, e.Unit })
                .Select(group =>
                {
                    var first = group.First();
                    return new BomRow
                    {
                        Category = first.Category, Family = first.Family, Type = first.Type,
                        Source = first.SourceName, RecordKeys = group.Select(e => e.Identity).ToList(),
                        System = first.System, Size = first.Size, Unit = first.Unit,
                        Quantity = group.Sum(e => e.Quantity), ElementCount = group.Count(),
                        ElementIds = string.Join(", ", group.Select(e => e.ElementId).OrderBy(id => id)),
                        ExtraValues = group.SelectMany(e => e.ExtraValues.Keys).Distinct().ToDictionary(key => key, key =>
                        {
                            var values = group.Select(e => e.ExtraValues.ContainsKey(key) ? e.ExtraValues[key] : "").Distinct().ToList();
                            return values.Count == 1 ? values[0] : "<varies>";
                        })
                    };
                }).OrderBy(r => r.Category).ThenBy(r => r.Family).ThenBy(r => r.Type)
                .ThenBy(r => r.System).ThenBy(r => r.Size).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].No = i + 1;
            return rows;
        }
    }
}
