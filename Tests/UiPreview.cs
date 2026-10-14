using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using MEP_Export_BOM_From_View_ver1;

internal static class UiPreview
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string assemblyPath = Path.GetFullPath(args[0]);
            string folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
            var assembly = Assembly.LoadFile(assemblyPath);
            var errors = new StringWriter();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            foreach (string state in new[] { "empty", "preview", "minimum", "schedule", "excel", "absent" })
            {
                Window window;
                using (var stream = assembly.GetManifestResourceStream("BomWindow.xaml"))
                    window = (Window)XamlReader.Load(stream);
                using (var stream = assembly.GetManifestResourceStream("BomSymbol.png"))
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                    Find<Image>(window, "HeaderSymbol").Source = image; Find<Image>(window, "PreviewSymbol").Source = image;
                }
                if (state != "empty")
                {
                    Find<ComboBox>(window, "ScopeCombo").SelectedIndex = 1;
                    Find<ComboBox>(window, "TargetCombo").ItemsSource = new[] { new { Name = "MEP Coordination - Level 02 [13248]" } };
                    Find<ComboBox>(window, "TargetCombo").SelectedIndex = 0;
                    Find<Button>(window, "PreviewButton").IsEnabled = true;
                }
                Find<TextBlock>(window, "ModelText").Text = "Office Building - MEP  ·  Choose a scope, select categories and review quantities.";
                Find<TextBox>(window, "CategorySearch").Text = "";
                var categories = new List<CategoryOption>
                {
                    new CategoryOption { Id = 1, Name = "Pipes", Count = 124, IsSelected = true },
                    new CategoryOption { Id = 2, Name = "Pipe Fittings", Count = 68, IsSelected = true },
                    new CategoryOption { Id = 3, Name = "Pipe Accessories", Count = 24, IsSelected = true },
                    new CategoryOption { Id = 4, Name = "Ducts", Count = 56, IsSelected = true },
                    new CategoryOption { Id = 5, Name = "Duct Fittings", Count = 32, IsSelected = true },
                    new CategoryOption { Id = 6, Name = "Mechanical Equipment", Count = 12, IsSelected = true },
                    new CategoryOption { Id = 7, Name = "Cable Trays", Count = 0 }
                };
                if (state != "empty")
                {
                    Find<ListBox>(window, "CategoryList").ItemsSource = categories.Where(c => c.IsAvailable).ToList();
                    Find<ListBox>(window, "AbsentCategoryList").ItemsSource = categories.Where(c => !c.IsAvailable).ToList();
                    Find<TextBlock>(window, "CategoryCountText").Text = "6 present / 6 selected";
                    Find<TextBlock>(window, "EmptyCategories").Visibility = Visibility.Collapsed;
                }
                else
                {
                    Find<ComboBox>(window, "TargetCombo").IsEnabled = false;
                    Find<TextBox>(window, "CategorySearch").IsEnabled = false;
                    foreach (string name in new[] { "MepButton", "AllButton", "NoneButton" }) Find<Button>(window, name).IsEnabled = false;
                }
                Find<Button>(window, "ClearTemplateButton").IsEnabled = false;
                Find<TextBox>(window, "SheetName").IsEnabled = false; Find<TextBox>(window, "FirstDataRow").IsEnabled = false;
                if (state != "empty")
                {
                    Find<StackPanel>(window, "EmptyPreview").Visibility = Visibility.Collapsed;
                    Find<TextBlock>(window, "ElementMetric").Text = "316"; Find<TextBlock>(window, "RowMetric").Text = "8";
                    Find<TextBlock>(window, "StatusText").Text = "316 elements / 8 BOM rows · View3D / MEP Coordination - Level 02";
                    Find<Button>(window, "ExportButton").IsEnabled = true; Find<Button>(window, "NotesButton").IsEnabled = true;
                    var rows = new List<BomRow>();
                    for (int i = 1; i <= 8; i++) rows.Add(new BomRow { No = i, Category = i < 4 ? "Pipes" : "Ducts", Family = i < 4 ? "Pipe Types" : "Rectangular Duct", Type = i < 4 ? "CHW - Steel" : "Galvanized", System = i < 4 ? "CHWS" : "Supply Air", Size = i < 4 ? "D=50 mm" : "W=400; H=200 mm", Unit = "m", Quantity = 48.235, ElementCount = 12, ElementIds = "12345, 12346" });
                    Find<DataGrid>(window, "BomGrid").ItemsSource = rows;
                    foreach (var column in Find<DataGrid>(window, "BomGrid").Columns.OfType<DataGridTextColumn>())
                    {
                        string path = (column.Binding as Binding)?.Path?.Path;
                        if (new[] { "System", "Size", "Unit", "Quantity", "ElementIds" }.Contains(path)) column.Visibility = Visibility.Collapsed;
                    }
                    Find<DataGrid>(window, "BomGrid").Columns.Add(ConfigurationDialogs.ElementIdColumn(window));
                    Find<Button>(window, "CreateScheduleButton").IsEnabled = true;
                    Find<Button>(window, "ModifyTitleButton").IsEnabled = true;
                    foreach (string name in new[] { "BomQueryButton", "ScheduleQueryButton", "ExportSchedulesButton", "ImportSelectionButton" }) Find<Button>(window, name).IsEnabled = true;
                }
                else Find<TextBlock>(window, "StatusText").Text = "Choose a scope and view, then click Preview BOM to load categories.";
                var tabs = Find<TabControl>(window, "PreviewTabs");
                if (tabs.Items.Count != 3) throw new Exception("BOM, Schedule and Excel/CSV tabs are required.");
                bool scheduleTab = state == "schedule", excelTab = state == "excel";
                Find<TextBlock>(window, "PreviewTitleText").Text = excelTab ? "Excel output and element selection" : scheduleTab ? "BOM - Pipes" : "MEP BILL OF MATERIALS";
                Find<Button>(window, "ModifyTitleButton").Visibility = excelTab ? Visibility.Collapsed : Visibility.Visible;
                Find<Button>(window, "ModifyTitleButton").Content = scheduleTab ? "Modify Schedule Title" : "Modify BOM Title";
                Find<Button>(window, "CreateScheduleButton").Visibility = scheduleTab ? Visibility.Visible : Visibility.Collapsed;
                Find<Button>(window, "ExportSchedulesButton").Visibility = scheduleTab ? Visibility.Visible : Visibility.Collapsed;
                Find<Button>(window, "ExportButton").Visibility = scheduleTab ? Visibility.Collapsed : Visibility.Visible;
                if (excelTab) tabs.SelectedIndex = 2;
                if (state == "absent") { Find<TabControl>(window, "CategoryPresenceTabs").SelectedIndex = 1; foreach (string name in new[] { "MepButton", "AllButton", "NoneButton" }) Find<Button>(window, name).IsEnabled = false; }
                if (excelTab) {
                    Find<Expander>(window, "ExcelOptionsExpander").IsExpanded = false;
                    var list = Find<DataGrid>(window, "ImportedElementsGrid");
                    foreach (string field in new[] { "ElementId", "Source", "Family", "Type" }) list.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = 150 });
                    list.Columns.Add(new DataGridTextColumn { Header = "Mark", Binding = new Binding("Values[param:mark]"), Width = 150 });
                    var sample = new ElementRecord { ElementId = 12345, SourceName = "Host", Family = "Pipe Types", Type = "CHW - Steel" }; sample.ExtraValues["param:mark"] = "P-001";
                    list.ItemsSource = ImportSelection.ElementRows(new[] { sample }, new[] { new ImportMapping { FileHeading = "Mark", FieldKey = "param:mark", ParameterName = "Mark" } });
                    Find<TextBlock>(window, "ImportedElementsCount").Text = "1 matched element · actual parameter values · Mark mapped from Excel";
                }
                if (state == "schedule")
                {
                    tabs.SelectedIndex = 1;
                    Find<ComboBox>(window, "ScheduleCategoryCombo").ItemsSource = categories;
                    Find<ComboBox>(window, "ScheduleCategoryCombo").SelectedIndex = 0;
                    Find<TextBlock>(window, "SchedulePreviewStatus").Text = "BOM - Pipes · Entire host model · Native Revit fields, units and totals · Read-only preview";
                    var scheduleGrid = Find<DataGrid>(window, "ScheduleGrid");
                    var headings = new[] { "Category", "Family", "Type", "Count" };
                    for (int col = 0; col < 4; col++) scheduleGrid.Columns.Add(new DataGridTextColumn { Header = headings[col], Binding = new Binding("[" + col + "]"), Width = col == 3 ? 100 : 220, MinWidth = 80 });
                    scheduleGrid.ItemsSource = new[] {
                        new[] { "Pipes", "Pipe Types", "CHW - Steel", "12" },
                        new[] { "Pipes", "Pipe Types", "HWS - Copper", "8" }
                    };
                    Find<Border>(window, "ScheduleTotalPanel").Visibility = Visibility.Visible; Find<TextBlock>(window, "ScheduleTotalText").Text = "Grand total · Count: 20";
                }
                var content = (FrameworkElement)window.Content;
                double width = state == "minimum" ? 1180 : 1480;
                double height = state == "minimum" ? 820 : 860;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                var render = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); render.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (var output = File.Create(Path.Combine(folder, state + ".png"))) encoder.Save(output);
                var check = Find<ListBox>(window, "CategoryList");
                int expected = state == "empty" ? 0 : 6;
                if (check.Items.Count != expected || (state != "absent" && check.ActualHeight < 80) || (!scheduleTab && Find<Button>(window, "ExportButton").ActualWidth < 100))
                    throw new Exception("Layout is clipped: " + state);
                if (state == "empty" && (Find<ComboBox>(window, "ScopeCombo").SelectedIndex != -1 || Find<ComboBox>(window, "TargetCombo").Items.Count != 0 || Find<Button>(window, "CreateScheduleButton").IsEnabled))
                    throw new Exception("Initial state contains preloaded selections.");
                if (state == "absent") { var list = Find<ListBox>(window, "AbsentCategoryList"); var item = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem; var checkbox = item == null ? null : Descendant<CheckBox>(item); if (list.Items.Count != 1 || checkbox == null || checkbox.IsEnabled) throw new Exception("Absent category must be visible and disabled in its own tab."); }
                if (excelTab && (Find<DataGrid>(window, "ImportedElementsGrid").Items.Count != 1 || Find<DataGrid>(window, "ImportedElementsGrid").ActualHeight < 140)) throw new Exception("Imported element list is missing or too short.");
                // Exercise two-way checkbox binding on the real template without a Revit document.
                var container = check.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                if (container != null)
                {
                    var checkbox = Descendant<CheckBox>(container);
                    if (checkbox != null) { checkbox.IsChecked = false; if (categories[0].IsSelected) throw new Exception("Category selection binding failed."); }
                }
                if (state == "preview")
                {
                    new WindowInteropHelper(window).EnsureHandle();
                    var names = ConfigurationDialogs.BuildNamesWindow(window, new List<ScheduleRequest>
                    {
                        new ScheduleRequest { CategoryId = 1, CategoryName = "Pipes", ScheduleName = "BOM - Pipes" },
                        new ScheduleRequest { CategoryId = 2, CategoryName = "Ducts", ScheduleName = "BOM - Ducts" },
                        new ScheduleRequest { CategoryId = 3, CategoryName = "Cable Trays", ScheduleName = "BOM - Cable Trays" }
                    }, new string[0]);
                    var selectionGrid = Descendant<DataGrid>((DependencyObject)names.Content); if (selectionGrid == null || Convert.ToString(selectionGrid.Columns[0].Header) != "Selection") throw new Exception("Schedule creation needs the Selection column first.");
                    Capture(names, folder, "schedule-names", 1100, 600); names.Close();
                    var titles = ConfigurationDialogs.BuildLayoutWindow(window, new BomLayout(), new List<ParameterField>
                    { new ParameterField { Key = "param:1", Name = "Comments" }, new ParameterField { Key = "param:2", Name = "Manufacturer" } }, accepted => { });
                    Capture(titles, folder, "modify-title", 1020, 680); titles.Close();
                    var fields = BomLayout.Defaults().ConvertAll(c => new ParameterField { Key = c.Key, Name = c.Header });
                    var query = ConfigurationDialogs.BuildQueryWindow(window, new PreviewSettings { Filters = new List<PreviewFilter> { new PreviewFilter { Field = "System", Operator = "Contains", Value = "CHW" } }, Sorts = new List<PreviewSort> { new PreviewSort { Field = "Type", Header = true, Footer = true } } }, fields, false, accepted => { });
                    Capture(query, folder, "filters", 1080, 740); query.Close();
                    var grouping = ConfigurationDialogs.BuildQueryWindow(window, new PreviewSettings { Sorts = new List<PreviewSort> { new PreviewSort { Field = "System", Header = true, Footer = true } } }, fields, true, accepted => { });
                    var queryTabs = Descendant<TabControl>((DependencyObject)grouping.Content); if (queryTabs != null) queryTabs.SelectedIndex = 1;
                    Capture(grouping, folder, "sorting-grouping", 1080, 740); grouping.Close();
                    var native = new NativeScheduleOptions { Title = "BOM - Pipes", Fields = new List<NativeField> { new NativeField { Key = "type", Name = "Family and Type", Heading = "Family and Type" }, new NativeField { Key = "length", Name = "Length", Heading = "Length", Total = true, CanTotal = true } } };
                    var nativeTitle = ConfigurationDialogs.BuildNativeLayoutWindow(window, native, accepted => { }); Capture(nativeTitle, folder, "schedule-title", 1040, 700); nativeTitle.Close();
                    var selection = ConfigurationDialogs.BuildScheduleSelection(window, new List<ScheduleRequest> { new ScheduleRequest { CategoryName = "Pipes", ScheduleName = "BOM - Pipes" }, new ScheduleRequest { CategoryName = "Ducts", ScheduleName = "BOM - Ducts" } }); Capture(selection, folder, "schedule-select", 820, 580); selection.Close();
                    var measurements = ConfigurationDialogs.BuildMeasurements(window, new List<MeasurementRule> { new MeasurementRule { Category = "Pipes", Method = "Length" }, new MeasurementRule { Category = "Walls", Method = "Area" } }, accepted => { }); Capture(measurements, folder, "measurements", 800, 600); measurements.Close();
                    var mapping = ConfigurationDialogs.BuildMapping(window, BomLayout.Defaults(), new List<TemplateMapping>(), 20, (accepted, total) => { }); Capture(mapping, folder, "template-mapping", 820, 660); mapping.Close();
                    var imported = ConfigurationDialogs.BuildImportWindow(window, new[] { ImportSelection.ReadCsv("Type,Comments\nCHW,example\nHWS,second\nmissing,unmatched") },
                        ImportSelection.Fields(new BomLayout(), new ParameterField[0]), new[] { new ElementRecord { ElementId = 1, Type = "CHW" }, new ElementRecord { ElementId = 2, Type = "HWS" } }, (matches, label) => { });
                    var importMatchButton = Descendants<Button>((DependencyObject)imported.Content).First(b => Convert.ToString(b.Content) == "Match / Preview");
                    importMatchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var importTable = Descendant<DataGrid>((DependencyObject)imported.Content);
                    if (importTable.Items.Count != 3 || !Descendants<Button>((DependencyObject)imported.Content).First(b => Convert.ToString(b.Content) == "Apply selection").IsEnabled)
                        throw new Exception("Import preview did not enable applying a validated match.");
                    Capture(imported, folder, "import-selection", 1340, 720); imported.Close();
                    var properties = ConfigurationDialogs.BuildPropertiesWindow(window, new[]
                    { new ElementRecord { ElementId = 12345, Family = "Pipe Types", Type = "CHW - Steel", SourceName = "Office Building - MEP" },
                      new ElementRecord { ElementId = 12346, Family = "Pipe Types", Type = "CHW - Steel", SourceName = "Office Building - MEP" } }, record => new List<ElementProperty>
                    {
                        new ElementProperty { Group = "Identity", Name = "Element ID", Value = record.ElementId.ToString(), Access = "Read-only" },
                        new ElementProperty { Group = "Identity", Name = "Category", Value = "Pipes", Access = "Read-only" },
                        new ElementProperty { Group = "BOM", Name = "System", Value = "CHWS", Access = "Read-only" },
                        new ElementProperty { Group = "BOM", Name = "Size", Value = "D=50 mm", Access = "Read-only" },
                        new ElementProperty { Group = "BOM", Name = "Quantity", Value = "48.235", Access = "Read-only" },
                        new ElementProperty { Group = "BOM", Name = "Unit", Value = "m", Access = "Read-only" },
                        new ElementProperty { Group = "Instance", Name = "Diameter", Value = "50 mm", Access = "Edit in Revit" },
                        new ElementProperty { Group = "Instance", Name = "Length", Value = "48.235 m", Access = "Read-only" },
                        new ElementProperty { Group = "Type", Name = "Material", Value = "Steel", Access = "Edit in Revit" }
                    });
                    Capture(properties, folder, "element-properties", 1240, 720); properties.Close();
                    var ids = ConfigurationDialogs.BuildElementIdsWindow(window, new[] {
                        new ElementRecord { ElementId = 12345, SourceName = "Host · Office MEP", Category = "Pipes", Type = "CHW - Steel", UniqueId = "sample-host-12345" },
                        new ElementRecord { ElementId = 12346, SourceName = "Host · Office MEP", Category = "Pipes", Type = "CHW - Steel", UniqueId = "sample-host-12346" },
                        new ElementRecord { ElementId = 12345, SourceName = "Link · Tower MEP", LinkInstanceId = 901, Category = "Pipes", Type = "CHW - Steel", UniqueId = "sample-link-12345" }
                    }, record => { }, records => { });
                    Capture(ids, folder, "element-id-list", 1360, 650); ids.Close();
                    var comparison = ConfigurationDialogs.BuildChanges(window, new List<BomChange> { new BomChange { Change = "Added", Source = "Host", Element = "123", Category = "Pipes", After = "CHW · 50 mm · 4 m", Unit = "m", Delta = 4 } }, () => { }); Capture(comparison, folder, "comparison", 1100, 680); comparison.Close();
                }
                window.Close();
            }
            Trace.Flush();
            if (!string.IsNullOrWhiteSpace(errors.ToString())) throw new Exception("WPF binding warnings: " + errors);
            Console.WriteLine("PASS: embedded XAML/icon, blank initial state, populated/minimum layouts, naming/fields dialogs, two-way category binding.");
            Console.WriteLine("UI previews: " + folder);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static T Find<T>(Window window, string name) where T : class
    { return window.FindName(name) as T ?? throw new Exception("Missing control " + name); }

    private static void Capture(Window window, string folder, string name, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var root = new Border { Background = window.Background, Resources = window.Resources, Child = content };
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(folder, name + ".png"))) encoder.Save(output);
    }

    private static T Descendant<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); if (child is T) return (T)child; var match = Descendant<T>(child); if (match != null) return match; }
        return null;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T) yield return (T)node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var match in Descendants<T>(child)) yield return match;
    }
}
