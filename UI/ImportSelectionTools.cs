using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using System.Windows.Input;

namespace MEP_Export_BOM_From_View_ver1
{
    internal sealed partial class ExportBomWindow
    {
        private HashSet<string> importedSelection;
        private HashSet<int> categoriesBeforeImport;
        private string importDescription;

        private void ImportElements()
        {
            if (scopeResult == null) return;
            var file = new OpenFileDialog { Filter = "Excel or CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv" };
            if (file.ShowDialog(window) != true) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var sheets = ImportSelection.Read(file.FileName);
                BomService.LoadCommonParameters(document, scopeResult);
                var fields = ImportSelection.Fields(layout, scopeResult.ParameterFields);
                // Include custom native schedule headings that point to a real parameter.
                foreach (var options in scheduleOptions.Values)
                    foreach (var native in options.Fields)
                    {
                        string id = native.Key.Split(':')[0];
                        var matches = fields.Where(f => f.Key == "param:" + id ||
                            (f.Key.StartsWith("param:", StringComparison.Ordinal) && f.Aliases.Contains(native.Name))).ToList();
                        if (matches.Count == 1) matches[0].Aliases.Add(native.Heading);
                    }
                Mouse.OverrideCursor = null;
                ConfigurationDialogs.BuildImportWindow(window, sheets, fields, scopeResult.Elements, (selection, description) =>
                {
                    if (categoriesBeforeImport == null) categoriesBeforeImport = new HashSet<int>(categoryOptions.Where(c => c.IsSelected).Select(c => c.Id));
                    importedSelection = new HashSet<string>(selection.Identities);
                    importDescription = System.IO.Path.GetFileName(file.FileName) + " / " + description;
                    var matchedCategories = new HashSet<int>(scopeResult.Elements.Where(e => importedSelection.Contains(e.Identity)).Select(e => e.CategoryId));
                    updating = true;
                    try { foreach (var category in categoryOptions) category.IsSelected = category.IsAvailable && matchedCategories.Contains(category.Id); }
                    finally { updating = false; }
                    Find<TextBlock>("ImportStatusText").Text = importDescription + "\n" + importedSelection.Count + " matched elements / " + selection.Rows.Count(r => r.Matches == 0) + " unmatched rows.";
                    Find<Button>("ClearImportButton").IsEnabled = true;
                    RebuildPreview();
                }).ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Import Excel / CSV", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void ClearImportedSelection(bool restoreCategories)
        {
            if (restoreCategories && categoriesBeforeImport != null)
            {
                updating = true;
                try { foreach (var category in categoryOptions) category.IsSelected = category.IsAvailable && categoriesBeforeImport.Contains(category.Id); }
                finally { updating = false; }
            }
            importedSelection = null; categoriesBeforeImport = null; importDescription = null;
            Find<TextBlock>("ImportStatusText").Text = "No import filter applied. Import values match within the current scope.";
            Find<Button>("ClearImportButton").IsEnabled = false;
            if (restoreCategories && scopeResult != null) RebuildPreview();
        }

        private void UpdateTabActions()
        {
            int tab = Find<TabControl>("PreviewTabs").SelectedIndex;
            var category = Find<ComboBox>("ScheduleCategoryCombo").SelectedItem as CategoryOption;
            bool nativeReady = result != null && category != null && scheduleTables.ContainsKey(category.Id);
            NativeScheduleOptions current;
            Find<TextBlock>("PreviewTitleText").Text = tab == 2 ? "Excel output and element selection" :
                tab == 1 ? category != null && scheduleOptions.TryGetValue(category.Id, out current) ? current.Title : "Revit Schedule Preview" :
                result?.Layout.Title ?? "Review before you export";
            Find<Button>("ModifyTitleButton").IsEnabled = tab == 0 ? result != null && result.Elements.Count > 0 : tab == 1 && nativeReady;
            Find<Button>("ModifyTitleButton").Content = tab == 1 ? "Modify Schedule Title" : "Modify BOM Title";
            Find<Button>("ModifyTitleButton").Visibility = tab < 2 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            Find<Button>("CreateScheduleButton").Visibility = tab == 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            Find<Button>("CreateScheduleButton").IsEnabled = tab == 1 && nativeReady;
            Find<Button>("ExportSchedulesButton").Visibility = tab == 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            Find<Button>("ExportButton").Visibility = tab != 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            Find<Button>("ImportSelectionButton").IsEnabled = scopeResult != null;
            Find<Button>("TemplateMappingButton").IsEnabled = result != null;
            Find<Button>("HighlightButton").IsEnabled = result != null && result.Elements.Count > 0 && tab == 0;
        }
    }
}
