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
        internal static void Apply(Document doc, ViewSchedule schedule, IList<int> ids)
        {
            if (schedule.Definition.GetFilterCount() >= 8) throw new InvalidOperationException("Scoped schedules reserve one Revit filter for scope membership; use at most 7 user filters.");
            var category = Category.GetCategory(doc, schedule.Definition.CategoryId);
            if (category == null || !category.AllowsBoundParameters) throw new InvalidOperationException("This category cannot support an exact scoped schedule parameter. Use Entire host model.");
            var shared = SharedParameterElement.Lookup(doc, MarkerGuid);
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
                    definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("MEP BOM Scope Membership", SpecTypeId.String.Text) { GUID = MarkerGuid, Visible = true, HideWhenNoValue = true, UserModifiable = false });
                    var categories = app.Create.NewCategorySet(); categories.Insert(category);
                    if (!doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(categories), BuiltInParameterGroup.PG_DATA)) throw new InvalidOperationException("Cannot bind schedule scope parameter.");
                    doc.Regenerate(); shared = SharedParameterElement.Lookup(doc, MarkerGuid); definition = shared.GetDefinition();
                } finally { app.SharedParametersFilename = previous; if (File.Exists(path)) File.Delete(path); }
            }
            var binding = doc.ParameterBindings.get_Item(definition) as InstanceBinding;
            if (binding == null) throw new InvalidOperationException("Scope membership parameter must be an instance binding.");
            if (!binding.Categories.Contains(category)) {
                var categories = doc.Application.Create.NewCategorySet(); foreach (Category existing in binding.Categories) categories.Insert(existing); categories.Insert(category);
                if (!doc.ParameterBindings.ReInsert(definition, doc.Application.Create.NewInstanceBinding(categories), BuiltInParameterGroup.PG_DATA)) throw new InvalidOperationException("Cannot extend scope membership binding.");
                doc.Regenerate();
            }
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
