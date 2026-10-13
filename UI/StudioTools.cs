using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using Visibility = System.Windows.Visibility;

namespace MEP_Export_BOM_From_View_ver1
{
    internal sealed partial class ExportBomWindow
    {
        private void RefreshModel()
        {
            if (scope.SelectedIndex < 0) { Status("Choose a scope before Refresh."); return; }
            ClearScopeResults(); Preview();
        }
        private void ShowStudioTools()
        {
            var menu = new ContextMenu { FontFamily = window.FontFamily, FontSize = 13, Background = window.Background };
            AddTool(menu, "Save preset…", SavePreset);
            AddTool(menu, "Load preset…", LoadPreset);
            menu.Items.Add(new Separator());
            AddTool(menu, "Measurement rules…", EditMeasurements, scopeResult != null);
            AddTool(menu, "Template column mapping…", EditMapping, result != null);
            AddTool(menu, "Quality / warnings…", ShowNotes, scopeResult != null);
            AddTool(menu, "Save BOM snapshot…", SaveSnapshot, result != null);
            AddTool(menu, "Compare with previous snapshot…", CompareSnapshot, result != null);
            menu.Items.Add(new Separator());
            AddTool(menu, "Export existing Revit schedules…", ExportExistingSchedules);
            menu.PlacementTarget = Find<Button>("StudioToolsButton"); menu.IsOpen = true;
        }
        private void AddTool(ContextMenu menu, string title, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += (s, e) => { try { action(); } catch (Exception ex) { MessageBox.Show(window, ex.Message, title); } }; menu.Items.Add(item);
        }
        private void SavePreset()
        {
            var save = new SaveFileDialog { Filter = "BOM preset (*.preset.xml)|*.preset.xml", FileName = "MEP_BOM.preset.xml" }; if (save.ShowDialog(window) != true) return;
            var schedules = scheduleOptions.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()); foreach (var options in schedules.Values) options.ScopeElementIds = null;
            int row; if (!int.TryParse(dataRow.Text, out row)) row = 6;
            StudioState.Save(save.FileName, new StudioPreset { Layout = layout.Clone(), Query = bomSettings.Clone(), Schedules = schedules,
                SelectedCategories = categoriesLoaded ? categoryOptions.Where(c => c.IsSelected).Select(c => c.Id).ToList() : selections.Where(p => p.Value).Select(p => p.Key).ToList(),
                Measurements = measurementRules, Mapping = templateMapping, TotalRow = templateTotalRow, Template = template.Text, Sheet = sheet.Text, DataRow = row,
                IncludeLinks = Find<CheckBox>("IncludeLinksCheck").IsChecked == true, MatchScope = Find<CheckBox>("MatchScopeCheck").IsChecked == true, CategorySheets = Find<CheckBox>("CategorySheetsCheck").IsChecked == true });
            Status("Preset saved: " + save.FileName);
        }
        private void LoadPreset()
        {
            var open = new OpenFileDialog { Filter = "BOM preset (*.preset.xml)|*.preset.xml" }; if (open.ShowDialog(window) != true) return;
            var preset = StudioState.Load<StudioPreset>(open.FileName);
            if (preset.Version != 1 || preset.Layout == null || preset.Query == null || preset.Schedules == null || preset.Measurements == null || preset.Mapping == null || preset.SelectedCategories == null || preset.DataRow < 1 || preset.DataRow > 1048576) throw new InvalidOperationException("Invalid or unsupported preset.");
            preset.Layout.Validate();
            var modelKeys = new HashSet<string> { BomService.ModelKey(document) };
            using (var links = new FilteredElementCollector(document)) foreach (var link in links.OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()) if (link.GetLinkDocument() != null) modelKeys.Add(BomService.ModelKey(link.GetLinkDocument()));
            var parameterKeys = preset.Layout.Columns.Select(c => c.Key).Concat(preset.Query.Filters.Select(f => f.Field)).Concat(preset.Query.Sorts.Select(s => s.Field));
            if (parameterKeys.Any(key => key != null && key.StartsWith("param:project:", StringComparison.Ordinal) && !modelKeys.Any(model => key.StartsWith("param:project:" + model + ":", StringComparison.Ordinal))))
                throw new InvalidOperationException("Preset uses project-specific parameters from another model. Reusable presets should use built-in parameters or shared parameters with the same GUID.");
            foreach (var pair in preset.Schedules) {
                if (!ViewSchedule.IsValidCategoryForSchedule(new ElementId(pair.Key))) throw new InvalidOperationException("Preset schedule category is not supported in this project.");
                var fresh = RevitScheduleService.GetOptions(document, pair.Key, Category.GetCategory(document, new ElementId(pair.Key))?.Name ?? "Category");
                var remap = new Dictionary<string, string>();
                foreach (var field in pair.Value.Fields) {
                    var matches = fresh.Available.Where(f => (field.Key.StartsWith("-", StringComparison.Ordinal) && f.Key == field.Key) || (f.Name == field.Name && f.Key.Split(':').Last() == field.Key.Split(':').Last())).ToList();
                    if (matches.Count != 1) throw new InvalidOperationException("Preset field cannot be resolved safely: " + field.Name + ". Use the original project or rebuild the preset.");
                    remap[field.Key] = matches[0].Key; field.Key = matches[0].Key;
                }
                foreach (var filter in pair.Value.Settings.Filters) { if (!remap.ContainsKey(filter.Field)) throw new InvalidOperationException("Preset filter references a missing field."); filter.Field = remap[filter.Field]; }
                foreach (var sort in pair.Value.Settings.Sorts) { if (!remap.ContainsKey(sort.Field)) throw new InvalidOperationException("Preset sort references a missing field."); sort.Field = remap[sort.Field]; }
                pair.Value.Available = fresh.Available; pair.Value.Settings.Validate(pair.Value.Fields.Select(f => f.Key), true);
            }
            foreach (var rule in preset.Measurements) if (!new[] { "Auto", "Count", "Length", "Area", "Volume" }.Contains(rule.Method)) throw new InvalidOperationException("Invalid measurement method.");
            foreach (var mapping in preset.Mapping) StudioState.ColumnNumber(mapping.Column);
            ClearScopeResults();
            updating = true;
            try {
                layout = preset.Layout; bomSettings = preset.Query; measurementRules = preset.Measurements; templateMapping = preset.Mapping; templateTotalRow = preset.TotalRow;
                scheduleOptions.Clear(); foreach (var pair in preset.Schedules) { pair.Value.ScopeElementIds = null; scheduleOptions[pair.Key] = pair.Value; }
                selections.Clear(); foreach (var category in document.Settings.Categories.Cast<Category>()) selections[category.Id.IntegerValue] = preset.SelectedCategories.Contains(category.Id.IntegerValue);
                template.Text = preset.Template ?? ""; sheet.Text = preset.Sheet ?? ""; dataRow.Text = preset.DataRow.ToString();
                Find<CheckBox>("IncludeLinksCheck").IsChecked = preset.IncludeLinks; Find<CheckBox>("MatchScopeCheck").IsChecked = preset.MatchScope; Find<CheckBox>("CategorySheetsCheck").IsChecked = preset.CategorySheets;
            } finally { updating = false; }
            if (scope.SelectedIndex >= 0) Preview(); Status("Preset loaded. Review shared parameter fields when using a different project.");
        }
        private void EditMeasurements()
        {
            var rules = categoryOptions.Where(c => c.IsAvailable).Select(c => new MeasurementRule { CategoryId = c.Id, Category = c.Name, Method = measurementRules.FirstOrDefault(r => r.CategoryId == c.Id)?.Method ?? "Auto" }).ToList();
            ConfigurationDialogs.BuildMeasurements(window, rules, accepted => { measurementRules = accepted; RebuildPreview(); }).ShowDialog();
        }
        private void EditMapping()
        {
            if (result == null) return;
            ConfigurationDialogs.BuildMapping(window, result.Layout.Columns, templateMapping, templateTotalRow, (mapping, total) => { templateMapping = mapping; templateTotalRow = total; RebuildPreview(); Status("Template mapping updated; choose a template, sheet and Data row."); }).ShowDialog();
        }
        private void HighlightRows()
        {
            if (result == null) return;
            var selected = grid.SelectedItems.Cast<BomRow>().Where(r => !r.IsDecoration).ToList();
            var keys = new HashSet<string>(selected.SelectMany(r => r.RecordKeys));
            if (keys.Count == 0) { MessageBox.Show(window, "Select one or more BOM data rows first.", "Highlight elements"); return; }
            HighlightRequests = result.Elements.Where(e => keys.Contains(e.Identity)).ToList(); window.Close();
        }
        private BomSnapshot Snapshot()
        {
            return new BomSnapshot { ModelKey = BomService.ModelKey(document), Scope = result.Scope, ExportedUtc = DateTime.UtcNow.ToString("o"), Elements = result.Elements,
                SelectionKey = string.Join(",", categoryOptions.Where(c => c.IsSelected).Select(c => c.Id).OrderBy(id => id)) + ";links=" + Find<CheckBox>("IncludeLinksCheck").IsChecked + ";" +
                    string.Join(";", measurementRules.Where(r => r.Method != "Auto").OrderBy(r => r.CategoryId).Select(r => r.CategoryId + ":" + r.Method)) + ";" +
                    string.Join(";", bomSettings.Filters.Select(f => f.Field + ":" + f.Operator + ":" + f.Value)) + ";itemized=" + bomSettings.Itemized };
        }
        private void SaveSnapshot()
        {
            var save = new SaveFileDialog { Filter = "BOM snapshot (*.bom.xml)|*.bom.xml", FileName = "BOM_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bom.xml" };
            if (save.ShowDialog(window) == true) { StudioState.Save(save.FileName, Snapshot()); Status("Snapshot saved: " + save.FileName); }
        }
        private void CompareSnapshot()
        {
            var open = new OpenFileDialog { Filter = "BOM snapshot (*.bom.xml)|*.bom.xml" }; if (open.ShowDialog(window) != true) return;
            var changes = StudioState.Compare(StudioState.Load<BomSnapshot>(open.FileName), Snapshot());
            ConfigurationDialogs.BuildChanges(window, changes, () => {
                var save = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "BOM_Changes.xlsx" }; if (save.ShowDialog(window) != true) return;
                var rows = new List<object[]> { new object[] { "Change", "Source", "Element", "Category", "Before", "After", "Unit", "Delta" } };
                rows.AddRange(changes.Select(c => new object[] { c.Change, c.Source, c.Element, c.Category, c.Before, c.After, c.Unit, c.Delta }));
                ExcelExporter.ExportSchedules(save.FileName, new Dictionary<string, List<object[]>> { { "BOM Changes", rows } });
            }).ShowDialog();
        }
        private void ExportExistingSchedules()
        {
            var byId = new Dictionary<int, ViewSchedule>();
            using (var collector = new FilteredElementCollector(document)) foreach (var schedule in collector.OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule)) byId[schedule.Id.IntegerValue] = schedule;
            var requests = byId.Values.OrderBy(s => s.Name).Select(s => new ScheduleRequest { CategoryId = s.Id.IntegerValue, CategoryName = "Existing Revit schedule", ScheduleName = s.Name }).ToList();
            if (requests.Count == 0) { MessageBox.Show(window, "No existing schedules in this model.", "Schedule export"); return; }
            if (ConfigurationDialogs.BuildScheduleSelection(window, requests).ShowDialog() != true) return;
            var save = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "Revit_Existing_Schedules.xlsx" }; if (save.ShowDialog(window) != true) return;
            var tables = new Dictionary<string, List<object[]>>();
            foreach (var request in requests.Where(r => r.IsSelected)) tables.Add(request.ScheduleName, RevitScheduleService.ReadExisting(byId[request.CategoryId]).Rows.Cast<System.Data.DataRow>().Select(r => r.ItemArray).ToList());
            ExcelExporter.ExportSchedules(save.FileName, tables); Status("Exported " + tables.Count + " existing schedules: " + save.FileName);
        }
        private IList<ParameterField> BomFilterValues(string field)
        {
            var selected = new HashSet<int>(categoryOptions.Where(c => c.IsSelected).Select(c => c.Id));
            return BomQuery.Apply(scopeResult.Elements.Where(e => selected.Contains(e.CategoryId)), new PreviewSettings { Itemized = bomSettings.Itemized }).Select(r => Convert.ToString(BomLayout.Value(r, field), CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v)
                .Select(v => new ParameterField { Key = v, Name = string.IsNullOrEmpty(v) ? "(empty)" : v }).ToList();
        }
    }
}
