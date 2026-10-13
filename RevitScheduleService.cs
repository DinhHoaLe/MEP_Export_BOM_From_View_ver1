using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEP_Export_BOM_From_View_ver1
{
    public static class RevitScheduleService
    {
        public static System.Data.DataTable Preview(Document document, int categoryId, string categoryName, NativeScheduleOptions options = null)
        {
            using (var group = new TransactionGroup(document, "Preview BOM schedule"))
            {
                group.Start();
                try
                {
                    var schedule = Create(document, categoryId, categoryName, "BOM Preview " + Guid.NewGuid().ToString("N"), options);
                    using (var transaction = new Transaction(document, "Regenerate schedule preview"))
                    { transaction.Start(); document.Regenerate(); transaction.Commit(); }
                    var body = schedule.GetTableData().GetSectionData(SectionType.Body);
                    var table = new System.Data.DataTable();
                    for (int col = 0; col < body.NumberOfColumns; col++) table.Columns.Add("Column" + col);
                    for (int row = body.FirstRowNumber; row <= body.LastRowNumber; row++)
                    {
                        var values = new object[body.NumberOfColumns];
                        for (int col = 0; col < values.Length; col++)
                            values[col] = schedule.GetCellText(SectionType.Body, row, body.FirstColumnNumber + col);
                        table.Rows.Add(values);
                    }
                    return table;
                }
                finally { group.RollBack(); }
            }
        }

        private static string Key(SchedulableField field) { return field.ParameterId.IntegerValue + ":" + field.FieldType; }
        private static string Key(ScheduleField field) { return field.ParameterId.IntegerValue + ":" + field.FieldType; }

        public static NativeScheduleOptions GetOptions(Document document, int categoryId, string categoryName)
        {
            using (var group = new TransactionGroup(document, "Read schedule fields"))
            {
                group.Start();
                try
                {
                    var schedule = Create(document, categoryId, categoryName, "BOM Fields " + Guid.NewGuid().ToString("N"));
                    var options = new NativeScheduleOptions { Title = "BOM - " + categoryName + " - Entire Model" };
                    var marker = SharedParameterElement.Lookup(document, ScopeScheduleMarker.MarkerGuid);
                    foreach (var field in schedule.Definition.GetSchedulableFields()) if (marker == null || field.ParameterId != marker.Id) options.Available.Add(new NativeField { Key = Key(field), Name = field.GetName(document), Heading = field.GetName(document) });
                    foreach (var id in schedule.Definition.GetFieldOrder())
                    {
                        var field = schedule.Definition.GetField(id);
                        options.Fields.Add(new NativeField { Key = Key(field), Name = field.GetName(), Heading = field.ColumnHeading, Visible = !field.IsHidden,
                            CanTotal = field.CanTotal(), Total = field.CanTotal() && field.DisplayType == ScheduleFieldDisplayType.Totals });
                    }
                    foreach (var available in options.Available)
                    {
                        var selected = options.Fields.FirstOrDefault(f => f.Key == available.Key);
                        if (selected != null) available.CanTotal = selected.CanTotal;
                    }
                    foreach (var selected in options.Fields.Where(f => options.Available.All(a => a.Key != f.Key))) options.Available.Add(selected.Clone());
                    if (options.Fields.Count > 0) options.Settings.Sorts.Add(new PreviewSort { Field = options.Fields[0].Key });
                    return options;
                }
                finally { group.RollBack(); }
            }
        }

        // Native schedules use category/model scope, not the arbitrary ID set from a view crop.
        public static IList<ViewSchedule> CreateBatch(Document document, IList<ScheduleRequest> requests)
        {
            var supported = requests.Where(r => r.Supported).ToList();
            ScheduleNames.Validate(supported, new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Select(v => v.Name));
            var schedules = new List<ViewSchedule>();
            using (var group = new TransactionGroup(document, "Create BOM category schedules"))
            {
                group.Start();
                foreach (var request in supported) schedules.Add(Create(document, request.CategoryId, request.CategoryName, request.ScheduleName, request.Options));
                if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Schedule batch was not committed.");
            }
            return schedules;
        }

        private static ViewSchedule Create(Document document, int categoryId, string categoryName, string scheduleName, NativeScheduleOptions options = null)
        {
            var id = new ElementId(categoryId);
            if (document.IsFamilyDocument || !ViewSchedule.IsValidCategoryForSchedule(id))
                throw new InvalidOperationException("This category cannot have a regular Revit schedule.");
            using (var transaction = new Transaction(document, "Create BOM category schedule"))
            {
                transaction.Start();
                var schedule = ViewSchedule.CreateSchedule(document, id);
                schedule.Name = scheduleName.Trim();
                var definition = schedule.Definition;
                var available = definition.GetSchedulableFields();
                var added = new HashSet<int>();
                ScheduleField first = AddField(definition, available, added, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM);
                if (first == null)
                {
                    first = AddField(definition, available, added, BuiltInParameter.ELEM_FAMILY_PARAM);
                    var type = AddField(definition, available, added, BuiltInParameter.ELEM_TYPE_PARAM);
                    if (first == null) first = type;
                }
                AddField(definition, available, added, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                var size = AddField(definition, available, added, BuiltInParameter.RBS_CALCULATED_SIZE) ??
                    AddField(definition, available, added, BuiltInParameter.RBS_PIPE_SIZE_FORMATTED_PARAM);
                foreach (var parameter in new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
                    BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM,
                    BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM })
                {
                    if (size != null) break;
                    var dimension = AddField(definition, available, added, parameter);
                    if (dimension != null) dimension.SetFormatOptions(new FormatOptions(UnitTypeId.Millimeters) { UseDefault = false, Accuracy = 0.1 });
                }
                var length = AddField(definition, available, added, BuiltInParameter.CURVE_ELEM_LENGTH);
                if (length != null)
                {
                    length.SetFormatOptions(new FormatOptions(UnitTypeId.Meters) { UseDefault = false, Accuracy = 0.001 });
                    if (length.CanTotal()) length.DisplayType = ScheduleFieldDisplayType.Totals;
                }
                definition.AddField(ScheduleFieldType.Count);
                definition.IsItemized = true;
                definition.ShowTitle = true; definition.ShowHeaders = true;
                definition.ShowGrandTotal = true; definition.ShowGrandTotalCount = true; definition.ShowGrandTotalTitle = true;
                if (first != null) definition.AddSortGroupField(new ScheduleSortGroupField(first.FieldId));
                if (options != null) Configure(document, definition, options);
                if (options?.ScopeElementIds != null) ScopeScheduleMarker.Apply(document, schedule, options.ScopeElementIds);
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the schedule creation.");
                return schedule;
            }
        }

        private static void Configure(Document document, ScheduleDefinition definition, NativeScheduleOptions options)
        {
            options.Settings.Validate(options.Fields.Select(f => f.Key), true);
            if (options.Fields.Count == 0 || !options.Fields.Any(f => f.Visible)) throw new InvalidOperationException("Choose at least one visible schedule field.");
            definition.ClearSortGroupFields(); definition.ClearFilters(); definition.ClearFields();
            var available = definition.GetSchedulableFields();
            var fields = new Dictionary<string, ScheduleField>();
            foreach (var item in options.Fields)
            {
                ScheduleField field;
                if (item.Key.EndsWith(":" + ScheduleFieldType.Count)) field = definition.AddField(ScheduleFieldType.Count);
                else
                {
                    var match = available.FirstOrDefault(f => Key(f) == item.Key);
                    if (match == null) throw new InvalidOperationException("Field is not available: " + item.Name);
                    field = definition.AddField(match);
                }
                field.ColumnHeading = item.Heading; field.IsHidden = !item.Visible;
                if (item.Total) { if (!field.CanTotal()) throw new InvalidOperationException("Cannot calculate totals for " + item.Name); field.DisplayType = ScheduleFieldDisplayType.Totals; }
                if (field.GetSpecTypeId() == SpecTypeId.Length)
                {
                    var units = item.Key.StartsWith(((int)BuiltInParameter.CURVE_ELEM_LENGTH) + ":") ? UnitTypeId.Meters : UnitTypeId.Millimeters;
                    field.SetFormatOptions(new FormatOptions(units) { UseDefault = false, Accuracy = units == UnitTypeId.Meters ? 0.001 : 0.1 });
                }
                fields.Add(item.Key, field);
            }
            foreach (var sort in options.Settings.Sorts)
            {
                var field = fields[sort.Field];
                if (!definition.CanSortByField(field.FieldId)) throw new InvalidOperationException("Cannot sort by " + field.ColumnHeading);
                var grouping = new ScheduleSortGroupField(field.FieldId, sort.Descending ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending) {
                    ShowHeader = sort.Header, ShowFooter = sort.Footer, ShowBlankLine = sort.BlankLine };
                // Revit rejects either footer setter while ShowFooter is false, even when assigning false.
                if (sort.Footer)
                {
                    grouping.ShowFooterTitle = sort.FooterTitle;
                    grouping.ShowFooterCount = sort.FooterCount;
                }
                definition.AddSortGroupField(grouping);
            }
            foreach (var rule in options.Settings.Filters)
            {
                var field = fields[rule.Field];
                var operation = (ScheduleFilterType)Enum.Parse(typeof(ScheduleFilterType), rule.Operator);
                ScheduleFilter filter;
                if (rule.Operator == "HasParameter" || rule.Operator == "HasValue" || rule.Operator == "HasNoValue") filter = new ScheduleFilter(field.FieldId, operation);
                else if (rule.Operator.Contains("GlobalParameter")) filter = new ScheduleFilter(field.FieldId, operation, new ElementId(int.Parse(rule.Value, System.Globalization.CultureInfo.InvariantCulture)));
                else if (rule.Operator.Contains("Contains") || rule.Operator.Contains("BeginsWith") || rule.Operator.Contains("EndsWith")) filter = new ScheduleFilter(field.FieldId, operation, rule.Value);
                else
                {
                    var storage = GetStorage(document, definition.CategoryId, field);
                    if (storage == StorageType.ElementId) filter = new ScheduleFilter(field.FieldId, operation, new ElementId(int.Parse(rule.Value, System.Globalization.CultureInfo.InvariantCulture)));
                    else if (storage == StorageType.Integer) filter = new ScheduleFilter(field.FieldId, operation, int.Parse(rule.Value, System.Globalization.CultureInfo.InvariantCulture));
                    else if (storage == StorageType.Double)
                    {
                        double value;
                        if (!UnitFormatUtils.TryParse(document.GetUnits(), field.GetSpecTypeId(), rule.Value, out value))
                            throw new InvalidOperationException("Enter a value in project units (or with a unit suffix) for " + field.ColumnHeading);
                        filter = new ScheduleFilter(field.FieldId, operation, value);
                    }
                    else filter = new ScheduleFilter(field.FieldId, operation, rule.Value);
                }
                try { definition.AddFilter(filter); }
                catch (Exception ex) { throw new InvalidOperationException("Filter is not supported for " + field.ColumnHeading + ": " + rule.Operator + ". " + ex.Message); }
            }
            definition.IsItemized = options.Settings.Itemized;
            definition.ShowGrandTotal = options.Settings.GrandTotal;
            if (options.Settings.GrandTotal)
            {
                definition.ShowGrandTotalTitle = options.Settings.GrandTotalTitle;
                definition.ShowGrandTotalCount = options.Settings.GrandTotalCount;
                if (options.Settings.GrandTotalTitle)
                    definition.GrandTotalTitle = options.Settings.TotalTitle ?? "Grand total";
            }
        }

        private static StorageType GetStorage(Document document, ElementId categoryId, ScheduleField field)
        {
            if (field.FieldType == ScheduleFieldType.Count) return StorageType.Integer;
            using (var collector = new FilteredElementCollector(document))
            {
                foreach (var element in collector.OfCategoryId(categoryId).WhereElementIsNotElementType())
                {
                    var source = field.FieldType == ScheduleFieldType.ElementType ? document.GetElement(element.GetTypeId()) : element;
                    var parameter = field.ParameterId.IntegerValue < 0 ? source?.get_Parameter((BuiltInParameter)field.ParameterId.IntegerValue) :
                        source?.Parameters.Cast<Parameter>().FirstOrDefault(p => p.Id == field.ParameterId);
                    if (parameter != null) return parameter.StorageType;
                }
            }
            if (field.GetSpecTypeId() == SpecTypeId.String.Text) return StorageType.String;
            return StorageType.Double;
        }

        private static ScheduleField AddField(ScheduleDefinition definition, IList<SchedulableField> available, HashSet<int> added, BuiltInParameter parameter)
        {
            int id = (int)parameter;
            if (added.Contains(id)) return null;
            var field = available.FirstOrDefault(f => f.ParameterId.IntegerValue == id);
            if (field == null) return null;
            var result = definition.AddField(field); added.Add(id);
            return result;
        }

        public static System.Data.DataTable ReadExisting(ViewSchedule schedule)
        {
            var body = schedule.GetTableData().GetSectionData(SectionType.Body);
            var table = new System.Data.DataTable(); for (int col = 0; col < body.NumberOfColumns; col++) table.Columns.Add("Column" + col);
            for (int row = body.FirstRowNumber; row <= body.LastRowNumber; row++) {
                var values = new object[body.NumberOfColumns]; for (int col = 0; col < values.Length; col++) values[col] = schedule.GetCellText(SectionType.Body, row, body.FirstColumnNumber + col); table.Rows.Add(values);
            }
            return table;
        }

        public static IList<ParameterField> FilterValues(Document document, int categoryId, string fieldKey, IList<int> scopeIds)
        {
            int parameterId; if (!int.TryParse((fieldKey ?? "").Split(':')[0], out parameterId)) return new List<ParameterField>();
            var values = new Dictionary<string, string>(); var scoped = scopeIds == null ? null : new HashSet<int>(scopeIds);
            using (var collector = new FilteredElementCollector(document)) foreach (var element in collector.OfCategoryId(new ElementId(categoryId)).WhereElementIsNotElementType()) {
                if (scoped != null && !scoped.Contains(element.Id.IntegerValue)) continue;
                var source = fieldKey.EndsWith(":" + ScheduleFieldType.ElementType) ? document.GetElement(element.GetTypeId()) : element;
                if (source == null) continue;
                var parameter = parameterId < 0 ? source.get_Parameter((BuiltInParameter)parameterId) : source.Parameters.Cast<Parameter>().FirstOrDefault(p => p.Id.IntegerValue == parameterId);
                if (parameter == null || !parameter.HasValue) continue;
                string value, label;
                if (parameter.StorageType == StorageType.ElementId) { value = parameter.AsElementId().IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture); label = value + " · " + (document.GetElement(parameter.AsElementId())?.Name ?? "(none)"); }
                else if (parameter.StorageType == StorageType.String) value = label = parameter.AsString() ?? "";
                else if (parameter.StorageType == StorageType.Integer) value = label = parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                else value = label = parameter.AsValueString() ?? "";
                values[value] = label;
            }
            return values.OrderBy(p => p.Value).Select(p => new ParameterField { Key = p.Key, Name = p.Value }).ToList();
        }
    }
}
