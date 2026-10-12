using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEP_Export_BOM_From_View_ver1
{
    public static class RevitScheduleService
    {
        // Native schedules use category/model scope, not the arbitrary ID set from a view crop.
        public static IList<ViewSchedule> CreateBatch(Document document, IList<ScheduleRequest> requests)
        {
            var supported = requests.Where(r => r.Supported).ToList();
            ScheduleNames.Validate(supported, new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Select(v => v.Name));
            var schedules = new List<ViewSchedule>();
            using (var group = new TransactionGroup(document, "Create BOM category schedules"))
            {
                group.Start();
                foreach (var request in supported) schedules.Add(Create(document, request.CategoryId, request.CategoryName, request.ScheduleName));
                if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Schedule batch was not committed.");
            }
            return schedules;
        }

        private static ViewSchedule Create(Document document, int categoryId, string categoryName, string scheduleName)
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
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the schedule creation.");
                return schedule;
            }
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
    }
}
