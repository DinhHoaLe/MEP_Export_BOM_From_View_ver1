using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEP_Export_BOM_From_View_ver1
{
    public static class BomService
    {
        public static readonly HashSet<int> DefaultCategories = new HashSet<int>(new[]
        {
            BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures
        }.Select(c => (int)c));

        public static BomResult Collect(Document doc, ExportScope scope, ElementId targetId, IList<ElementId> categories)
        {
            if (categories.Count == 0) throw new InvalidOperationException("Select at least one category.");
            var result = new BomResult { Model = doc.Title };
            string scopeLabel;
            List<string> notes;
            var elements = GetScopeElements(doc, scope, targetId, categories, out scopeLabel, out notes);
            result.Scope = scopeLabel;
            result.Warnings.AddRange(notes);
            foreach (Element element in elements)
            {
                try
                {
                    var record = Read(doc, element);
                    result.Elements.Add(record);
                    if (!string.IsNullOrEmpty(record.Note)) result.Warnings.Add("Element " + record.ElementId + ": " + record.Note);
                }
                catch (Exception ex)
                { result.Warnings.Add("Skipped element " + element.Id.IntegerValue + ": " + ex.Message); }
            }
            result.Rows = BomAggregator.Aggregate(result.Elements);
            return result;
        }

        public static List<CategoryOption> GetAvailableCategories(Document doc, ExportScope scope, ElementId targetId, out List<string> notes)
        {
            var modelCategories = new Dictionary<int, Category>();
            foreach (Category category in doc.Settings.Categories)
                if (category.CategoryType == CategoryType.Model && category.Parent == null &&
                    category.Id.IntegerValue != (int)BuiltInCategory.OST_RvtLinks &&
                    category.Id.IntegerValue != (int)BuiltInCategory.OST_VolumeOfInterest)
                    modelCategories[category.Id.IntegerValue] = category;
            if (modelCategories.Count == 0) { notes = new List<string>(); return new List<CategoryOption>(); }
            string label;
            var elements = GetScopeElements(doc, scope, targetId, modelCategories.Keys.Select(id => new ElementId(id)).ToList(), out label, out notes);
            return elements.Where(e => e.Category != null).GroupBy(e => e.Category.Id.IntegerValue)
                .Select(group => new CategoryOption { Id = group.Key, Name = modelCategories[group.Key].Name, Count = group.Count(),
                    IsSelected = DefaultCategories.Contains(group.Key) }).OrderBy(c => c.Name).ToList();
        }

        // Category discovery and export share this exact collector and boundary test.
        private static List<Element> GetScopeElements(Document doc, ExportScope scope, ElementId targetId,
            IList<ElementId> categories, out string scopeLabel, out List<string> notes)
        {
            notes = new List<string>();
            View view = null;
            Element target = null;
            if (scope != ExportScope.EntireModel)
            {
                target = doc.GetElement(targetId);
                if (target == null) throw new InvalidOperationException("Select a valid view or Scope Box.");
                view = target as View;
                if (scope != ExportScope.ScopeBox && (view == null || view.IsTemplate ||
                    !FilteredElementCollector.IsViewValidForElementIteration(doc, view.Id)))
                    throw new InvalidOperationException("This view cannot be used for element collection.");
                if (scope == ExportScope.ScopeBox && (target.Category == null ||
                    target.Category.Id.IntegerValue != (int)BuiltInCategory.OST_VolumeOfInterest))
                    throw new InvalidOperationException("The selected element is not a Scope Box.");
                if (scope == ExportScope.View3D && !(view is View3D))
                    throw new InvalidOperationException("Select a 3D view.");
                if (scope == ExportScope.FloorPlan && view.ViewType != ViewType.FloorPlan)
                    throw new InvalidOperationException("Select a floor plan.");
            }
            scopeLabel = scope + (target == null ? "" : " / " + target.Name);
            notes.Add("Host model only; linked models are excluded. Full element quantities are used, including elements crossing the boundary.");
            Func<Element, bool> spatial = e => true;
            if (scope == ExportScope.ScopeBox)
            {
                spatial = SpatialFilter.ForScopeBox(target);
                notes.Add("Scope Box uses bounding-box intersection, including rotated boxes. Geometry bounding boxes may include empty space.");
            }
            else if (view != null)
            {
                spatial = SpatialFilter.ForView(view);
                notes.Add("View collection follows Revit view settings. Bounding boxes refine crop/section boundaries; this is not pixel visibility or geometry clipping.");
            }

            var filtered = new List<Element>();
            using (var collector = view == null ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, view.Id))
            {
                var elements = collector.WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(categories)).ToElements();
                foreach (Element element in elements)
                {
                    try
                    {
                        if (!spatial(element)) continue;
                        filtered.Add(element);
                    }
                    catch (Exception ex)
                    {
                        notes.Add("Skipped element " + element.Id.IntegerValue + ": " + ex.Message);
                    }
                }
            }
            return filtered;
        }

        public static void LoadCommonParameters(Document doc, BomResult result)
        {
            var names = new Dictionary<string, string>();
            var typeCache = new Dictionary<int, Dictionary<string, string>>();
            HashSet<string> common = null;
            foreach (var record in result.Elements)
            {
                var element = doc.GetElement(new ElementId(record.ElementId));
                var values = new Dictionary<string, string>();
                if (element != null)
                {
                    int typeId = element.GetTypeId().IntegerValue;
                    Dictionary<string, string> typeValues;
                    if (!typeCache.TryGetValue(typeId, out typeValues))
                    {
                        typeValues = new Dictionary<string, string>();
                        ReadParameters(doc.GetElement(element.GetTypeId()), typeValues, names);
                        typeCache[typeId] = typeValues;
                    }
                    foreach (var pair in typeValues) values[pair.Key] = pair.Value;
                    ReadParameters(element, values, names);
                }
                record.ExtraValues = values;
                if (common == null) common = new HashSet<string>(values.Keys);
                else common.IntersectWith(values.Keys);
            }
            result.CommonParameters = (common ?? new HashSet<string>()).Select(key => new ParameterField { Key = key, Name = names[key] })
                .OrderBy(f => f.Name).ThenBy(f => f.Key).ToList();
            result.Rows = BomAggregator.Aggregate(result.Elements);
        }

        private static void ReadParameters(Element element, Dictionary<string, string> values, Dictionary<string, string> names)
        {
            if (element == null) return;
            foreach (Parameter parameter in element.Parameters)
            {
                try
                {
                    if (parameter.Definition == null || parameter.StorageType == StorageType.None) continue;
                    string key = "param:" + parameter.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);
                    string value = parameter.HasValue ? Text(parameter) : "";
                    if (parameter.HasValue && parameter.StorageType == StorageType.ElementId)
                        value = element.Document.GetElement(parameter.AsElementId())?.Name ?? value;
                    if (parameter.HasValue && parameter.StorageType == StorageType.Integer && string.IsNullOrEmpty(value))
                        value = parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                    values[key] = value;
                    names[key] = parameter.Definition.Name;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* Parameter unavailable on this element. */ }
            }
        }

        private static ElementRecord Read(Document doc, Element element)
        {
            var type = doc.GetElement(element.GetTypeId()) as ElementType;
            var familyInstance = element as FamilyInstance;
            var record = new ElementRecord
            {
                ElementId = element.Id.IntegerValue, CategoryId = element.Category.Id.IntegerValue,
                TypeId = element.GetTypeId().IntegerValue, Category = element.Category.Name,
                Family = type != null ? type.FamilyName : familyInstance != null ? familyInstance.Symbol.Family.Name : "",
                Type = type == null ? element.Name : type.Name,
                System = Text(element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)),
                Unit = "pcs", Quantity = 1, Size = "", SizeKey = "", Note = ""
            };
            if (string.IsNullOrWhiteSpace(record.System) && familyInstance != null && familyInstance.MEPModel != null)
            {
                var manager = familyInstance.MEPModel.ConnectorManager;
                if (manager != null)
                {
                    var systems = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (Connector connector in manager.Connectors)
                    {
                        if (connector.ConnectorType != ConnectorType.End) continue;
                        try { if (connector.MEPSystem != null) systems.Add(connector.MEPSystem.Name); }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        { record.Note += " Connector system unavailable."; }
                    }
                    record.System = string.Join("; ", systems);
                }
            }

            BuiltInParameter[] dimensions =
            {
                BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
                BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, BuiltInParameter.RBS_CURVE_WIDTH_PARAM,
                BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM,
                BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM
            };
            var labels = new List<string>();
            var keys = new List<string>();
            foreach (var parameterId in dimensions)
            {
                var parameter = element.get_Parameter(parameterId) ?? (type == null ? null : type.get_Parameter(parameterId));
                if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double || parameter.AsDouble() <= 0) continue;
                double value = parameter.AsDouble();
                string label = parameterId == BuiltInParameter.RBS_CURVE_WIDTH_PARAM || parameterId == BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM ? "W" :
                    parameterId == BuiltInParameter.RBS_CURVE_HEIGHT_PARAM || parameterId == BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM ? "H" : "D";
                labels.Add(label + "=" + UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture));
                keys.Add(((int)parameterId).ToString(CultureInfo.InvariantCulture) + "=" + value.ToString("R", CultureInfo.InvariantCulture));
            }
            record.Size = string.Join("; ", labels) + (labels.Count > 0 ? " mm" : "");
            record.SizeKey = string.Join(";", keys);
            if (familyInstance != null && familyInstance.MEPModel != null)
            {
                var connectorSizes = new SortedSet<string>(StringComparer.Ordinal);
                var connectorKeys = new SortedSet<string>(StringComparer.Ordinal);
                var manager = familyInstance.MEPModel.ConnectorManager;
                if (manager != null)
                    foreach (Connector connector in manager.Connectors)
                    {
                        if (connector.ConnectorType != ConnectorType.End) continue;
                        try
                        {
                            double a, b = 0;
                            if (connector.Shape == ConnectorProfileType.Round) a = connector.Radius * 2;
                            else if (connector.Shape == ConnectorProfileType.Rectangular || connector.Shape == ConnectorProfileType.Oval)
                            { a = connector.Width; b = connector.Height; }
                            else continue;
                            connectorSizes.Add(connector.Shape + ":" + Millimeters(a) + (b > 0 ? "x" + Millimeters(b) : ""));
                            connectorKeys.Add(connector.Shape + ":" + a.ToString("R", CultureInfo.InvariantCulture) + ":" + b.ToString("R", CultureInfo.InvariantCulture));
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        { record.Note += " Connector size unavailable."; }
                    }
                if (connectorSizes.Count > 0)
                {
                    record.Size = string.Join(" / ", connectorSizes) + " mm";
                    record.SizeKey = string.Join("/", connectorKeys);
                }
            }

            if (element is MEPCurve)
            {
                record.Unit = "m";
                var length = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (length != null && length.HasValue && length.StorageType == StorageType.Double)
                    record.Quantity = UnitUtils.ConvertFromInternalUnits(length.AsDouble(), UnitTypeId.Meters);
                else
                {
                    var location = element.Location as LocationCurve;
                    if (location == null) throw new InvalidOperationException("Length could not be read.");
                    record.Quantity = UnitUtils.ConvertFromInternalUnits(location.Curve.Length, UnitTypeId.Meters);
                    record.Note = "Length read from LocationCurve.";
                }
            }
            if (DefaultCategories.Contains(record.CategoryId) && string.IsNullOrEmpty(record.Size) && element is MEPCurve)
                record.Note += " Size is missing; check element parameters.";
            return record;
        }

        private static string Millimeters(double value)
        { return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Text(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return "";
            return parameter.StorageType == StorageType.String ? parameter.AsString() ?? "" : parameter.AsValueString() ?? "";
        }
    }
}
