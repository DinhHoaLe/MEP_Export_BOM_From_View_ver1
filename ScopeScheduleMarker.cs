using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEP_Export_BOM_From_View_ver1
{
    internal static class ScopeScheduleMarker
    {
        internal static readonly Guid MarkerGuid = new Guid("7eed760d-fba1-4708-99d3-b6a753bd172e");
        internal static readonly Guid CategoryGuid = new Guid("53c01738-3540-444c-b9e3-faa3d70f7764");
        internal static readonly Guid FamilyGuid = new Guid("d2e946dc-9816-40d4-8f2f-00b0827f80ad");
        internal static readonly Guid TypeGuid = new Guid("4f4d4051-df22-4a72-92d4-450d884e4c40");
        internal static SharedParameterElement EnsureParameter(Document doc, Category category, Guid guid, string name)
        {
            if (category == null || !category.AllowsBoundParameters) throw new InvalidOperationException("This category cannot bind the BOM schedule support parameter.");
            var shared = SharedParameterElement.Lookup(doc, guid);
            Definition definition;
            if (shared != null) definition = shared.GetDefinition();
            else {
                var app = doc.Application; string previous = app.SharedParametersFilename;
                string path = Path.Combine(Path.GetTempPath(), "bom-scope-" + Guid.NewGuid().ToString("N") + ".txt");
                try {
                    File.WriteAllText(path, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n");
                    app.SharedParametersFilename = path;
                    var file = app.OpenSharedParameterFile(); if (file == null) throw new InvalidOperationException("Cannot prepare schedule scope parameter.");
                    var group = file.Groups.Create("MEP BOM Studio");
                    definition = group.Definitions.Create(new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text) { GUID = guid, Visible = true, HideWhenNoValue = true, UserModifiable = false });
                    var categories = app.Create.NewCategorySet(); categories.Insert(category);
                    if (!doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(categories), BuiltInParameterGroup.PG_DATA)) throw new InvalidOperationException("Cannot bind schedule scope parameter.");
                    doc.Regenerate(); shared = SharedParameterElement.Lookup(doc, guid); definition = shared.GetDefinition();
                } finally { app.SharedParametersFilename = previous; if (File.Exists(path)) File.Delete(path); }
            }
            var binding = doc.ParameterBindings.get_Item(definition) as InstanceBinding;
            if (binding == null) throw new InvalidOperationException("Scope membership parameter must be an instance binding.");
            if (!binding.Categories.Contains(category)) {
                var categories = doc.Application.Create.NewCategorySet(); foreach (Category existing in binding.Categories) categories.Insert(existing); categories.Insert(category);
                if (!doc.ParameterBindings.ReInsert(definition, doc.Application.Create.NewInstanceBinding(categories), BuiltInParameterGroup.PG_DATA)) throw new InvalidOperationException("Cannot extend scope membership binding.");
                doc.Regenerate();
            }
            return shared;
        }
        internal static ScheduleField CategoryField(Document doc, ViewSchedule schedule, string categoryName)
        { return LabelField(doc, schedule, CategoryGuid, "MEP BOM Category", "Category", element => categoryName); }
        internal static ScheduleField FamilyField(Document doc, ViewSchedule schedule)
        { return LabelField(doc, schedule, FamilyGuid, "MEP BOM Family", "Family", element => (doc.GetElement(element.GetTypeId()) as ElementType)?.FamilyName ?? ""); }
        internal static ScheduleField TypeField(Document doc, ViewSchedule schedule)
        { return LabelField(doc, schedule, TypeGuid, "MEP BOM Type", "Type", element => doc.GetElement(element.GetTypeId())?.Name ?? ""); }
        private static ScheduleField LabelField(Document doc, ViewSchedule schedule, Guid guid, string name, string heading, Func<Element, string> value)
        {
            var category = Category.GetCategory(doc, schedule.Definition.CategoryId);
            var shared = EnsureParameter(doc, category, guid, name);
            using (var collector = new FilteredElementCollector(doc)) foreach (var element in collector.OfCategoryId(category.Id).WhereElementIsNotElementType()) {
                var parameter = element.get_Parameter(guid); string text = value(element) ?? "";
                if (parameter == null) throw new InvalidOperationException(heading + " label parameter is unavailable on element " + element.Id.IntegerValue);
                if ((parameter.AsString() ?? "") == text) continue;
                if (parameter.IsReadOnly || !parameter.Set(text)) throw new InvalidOperationException("Cannot assign schedule " + heading + " for element " + element.Id.IntegerValue + ". Check group/worksharing ownership.");
            }
            var field = schedule.Definition.AddField(ScheduleFieldType.Instance, shared.Id); field.ColumnHeading = heading; return field;
        }
        internal static void Apply(Document doc, ViewSchedule schedule, IList<int> ids)
        {
            if (schedule.Definition.GetFilterCount() >= 8) throw new InvalidOperationException("Scoped schedules reserve one Revit filter for scope membership; use at most 7 user filters.");
            var category = Category.GetCategory(doc, schedule.Definition.CategoryId);
            var shared = EnsureParameter(doc, category, MarkerGuid, "MEP BOM Scope Membership");
            var token = "|" + Guid.NewGuid().ToString("N") + "|";
            foreach (int id in ids.Distinct()) {
                var element = doc.GetElement(new ElementId(id)); if (element == null || element.Category?.Id != category.Id) continue;
                var parameter = element.get_Parameter(MarkerGuid);
                if (parameter == null || parameter.IsReadOnly || !parameter.Set((parameter.AsString() ?? "") + token)) throw new InvalidOperationException("Cannot mark scope membership for element " + id + ". Check group/worksharing ownership.");
            }
            var available = schedule.Definition.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == shared.Id);
            if (available == null) throw new InvalidOperationException("Scope membership field is not schedulable.");
            var field = schedule.Definition.AddField(available); field.IsHidden = true;
            schedule.Definition.AddFilter(new ScheduleFilter(field.FieldId, ScheduleFilterType.Contains, token));
        }
    }
}
