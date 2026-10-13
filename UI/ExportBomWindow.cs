using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using RevitView = Autodesk.Revit.DB.View;
using WpfWindow = System.Windows.Window;
using Visibility = System.Windows.Visibility;
using Binding = System.Windows.Data.Binding;

namespace MEP_Export_BOM_From_View_ver1
{
    internal sealed partial class ExportBomWindow
    {
        private readonly Document document;
        private readonly WpfWindow window;
        private readonly ComboBox scope;
        private readonly ComboBox target;
        private readonly ListBox categories;
        private readonly DataGrid grid;
        private readonly Button previewButton;
        private readonly Button exportButton;
        private readonly Button notesButton;
        private readonly TextBox search;
        private readonly TextBox template;
        private readonly TextBox sheet;
        private readonly TextBox dataRow;
        private readonly Dictionary<int, bool> selections = new Dictionary<int, bool>();
        private List<CategoryOption> categoryOptions = new List<CategoryOption>();
        private ICollectionView categoryView;
        private List<string> scanNotes = new List<string>();
        private BomResult result;
        private BomResult scopeResult;
        private PreviewSettings bomSettings = new PreviewSettings();
        private readonly Dictionary<int, NativeScheduleOptions> scheduleOptions = new Dictionary<int, NativeScheduleOptions>();
        private readonly Dictionary<int, System.Data.DataTable> scheduleTables = new Dictionary<int, System.Data.DataTable>();
        private bool updating;
        private bool categoriesLoaded;
        private BomLayout layout = new BomLayout();
        private List<MeasurementRule> measurementRules = new List<MeasurementRule>();
        private List<TemplateMapping> templateMapping = new List<TemplateMapping>();
        private int templateTotalRow;
        public List<ElementRecord> HighlightRequests { get; private set; } = new List<ElementRecord>();
        public List<ScheduleRequest> ScheduleRequests { get; private set; } = new List<ScheduleRequest>();

        public ExportBomWindow(Document doc)
        {
            document = doc;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BomWindow.xaml"))
            {
                if (stream == null) throw new InvalidOperationException("BOM window resource is missing.");
                window = (WpfWindow)XamlReader.Load(stream);
            }
            scope = Find<ComboBox>("ScopeCombo"); target = Find<ComboBox>("TargetCombo");
            categories = Find<ListBox>("CategoryList"); grid = Find<DataGrid>("BomGrid");
            previewButton = Find<Button>("PreviewButton"); exportButton = Find<Button>("ExportButton"); notesButton = Find<Button>("NotesButton");
            search = Find<TextBox>("CategorySearch"); template = Find<TextBox>("TemplatePath");
            sheet = Find<TextBox>("SheetName"); dataRow = Find<TextBox>("FirstDataRow");
            Find<TextBlock>("ModelText").Text = doc.Title + "  ·  Choose a scope and view, then click Preview BOM.";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BomSymbol.png"))
            {
                if (stream != null)
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream; image.EndInit(); image.Freeze();
                    window.Icon = image;
                    Find<Image>("HeaderSymbol").Source = image; Find<Image>("PreviewSymbol").Source = image;
                }
            }
            scope.SelectedIndex = -1;
            scope.SelectionChanged += (s, e) => { if (!updating) FillTargetsAndCategories(); };
            target.SelectionChanged += (s, e) => { if (!updating) ClearScopeResults(); };
            search.TextChanged += (s, e) => { categoryView?.Refresh(); UpdateCategorySummary(); };
            Find<Button>("MepButton").Click += (s, e) => SetSelection("MEP");
            Find<Button>("AllButton").Click += (s, e) => SetSelection("All");
            Find<Button>("NoneButton").Click += (s, e) => SetSelection("None");
            previewButton.Click += (s, e) => Preview();
            Find<TabControl>("PreviewTabs").SelectionChanged += (s, e) =>
            { if (e.Source == Find<TabControl>("PreviewTabs")) { Find<Button>("HighlightButton").IsEnabled = result != null && Find<TabControl>("PreviewTabs").SelectedIndex == 0; if (Find<TabControl>("PreviewTabs").SelectedIndex == 1) PreviewSchedule(); } };
            Find<ComboBox>("ScheduleCategoryCombo").SelectionChanged += (s, e) => PreviewSchedule();
            exportButton.Click += (s, e) => Export();
            notesButton.Click += (s, e) => ShowNotes();
            Find<Button>("CreateScheduleButton").Click += (s, e) => { try { RequestSchedule(); } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Create schedules"); } };
            Find<Button>("ModifyTitleButton").Click += (s, e) => ModifyTitle();
            Find<Button>("ScheduleTitleButton").Click += (s, e) => ModifyScheduleTitle();
            Find<Button>("BomQueryButton").Click += (s, e) => ConfigureBomQuery();
            Find<Button>("ScheduleQueryButton").Click += (s, e) => ConfigureScheduleQuery();
            Find<Button>("ExportSchedulesButton").Click += (s, e) => ExportSchedules();
            Find<Button>("RefreshButton").Click += (s, e) => RefreshModel();
            Find<Button>("StudioToolsButton").Click += (s, e) => ShowStudioTools();
            Find<Button>("HighlightButton").Click += (s, e) => HighlightRows();
            Find<CheckBox>("IncludeLinksCheck").Checked += (s, e) => { if (!updating) ClearScopeResults(); };
            Find<CheckBox>("IncludeLinksCheck").Unchecked += (s, e) => { if (!updating) ClearScopeResults(); };
            Find<CheckBox>("MatchScopeCheck").Checked += (s, e) => { if (!updating) { scheduleTables.Clear(); PreviewSchedule(); } };
            Find<CheckBox>("MatchScopeCheck").Unchecked += (s, e) => { if (!updating) { scheduleTables.Clear(); PreviewSchedule(); } };
            grid.Sorting += (s, e) => {
                if (result == null) return;
                e.Handled = true;
                var key = e.Column.SortMemberPath;
                var previous = bomSettings.Sorts.FirstOrDefault(r => r.Field == key);
                bool descending = previous != null && !previous.Descending;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) bomSettings.Sorts.Clear();
                else if (previous != null) bomSettings.Sorts.Remove(previous);
                if (bomSettings.Sorts.Count < 4) bomSettings.Sorts.Add(new PreviewSort { Field = key, Descending = descending });
                RebuildPreview();
            };
            Find<DataGrid>("ScheduleGrid").Sorting += (s, e) => {
                e.Handled = true;
                var category = Find<ComboBox>("ScheduleCategoryCombo").SelectedItem as CategoryOption;
                if (category == null) return;
                try {
                    var options = GetScheduleOptions(category).Clone(); var previous = options.Settings.Sorts.FirstOrDefault(r => r.Field == e.Column.SortMemberPath);
                    bool descending = previous != null && !previous.Descending;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) options.Settings.Sorts.Clear();
                    else if (previous != null) options.Settings.Sorts.Remove(previous);
                    if (options.Settings.Sorts.Count < 4) options.Settings.Sorts.Add(new PreviewSort { Field = e.Column.SortMemberPath, Descending = descending });
                    var table = RevitScheduleService.Preview(document, category.Id, category.Name, options);
                    scheduleOptions[category.Id] = options; scheduleTables[category.Id] = table; PreviewSchedule();
                } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Schedule sorting"); }
            };
            Find<Button>("BrowseButton").Click += (s, e) =>
            {
                var dialog = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx" };
                if (dialog.ShowDialog(window) == true) template.Text = dialog.FileName;
            };
            Find<Button>("ClearTemplateButton").Click += (s, e) => template.Clear();
            template.TextChanged += (s, e) => UpdateTemplateControls();
            UpdateTemplateControls();
            Find<Button>("CloseButton").Click += (s, e) => window.Close();
            Find<Button>("MinimizeButton").Click += (s, e) => window.WindowState = WindowState.Minimized;
            Find<Button>("MaximizeButton").Click += (s, e) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            window.StateChanged += (s, e) =>
            {
                // Keep resize chrome inside the work area when maximized.
                var border = window.Content as Border;
                if (border != null) border.Margin = window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };
            window.Loaded += (s, e) => { target.IsEnabled = false; ClearScopeResults(); };
        }

        public void ShowDialog(IntPtr owner)
        { new WindowInteropHelper(window).Owner = owner; window.ShowDialog(); }

        private T Find<T>(string name) where T : class
        { return window.FindName(name) as T ?? throw new InvalidOperationException("Missing UI control: " + name); }

        private ElementId TargetId
        { get { var choice = target.SelectedItem as TargetChoice; return choice == null ? ElementId.InvalidElementId : choice.Id; } }

        private void FillTargetsAndCategories()
        {
            try
            {
                updating = true;
                target.ItemsSource = null;
                target.IsEnabled = scope.SelectedIndex != 0;
                var items = new List<TargetChoice>();
                if (scope.SelectedIndex == 3)
                {
                    using (var collector = new FilteredElementCollector(document))
                        items.AddRange(collector.OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType()
                            .Select(e => new TargetChoice(e.Id, e.Name + " [" + e.Id.IntegerValue + "]")));
                }
                else if (scope.SelectedIndex > 0)
                {
                    using (var collector = new FilteredElementCollector(document))
                        foreach (var view in collector.OfClass(typeof(RevitView)).Cast<RevitView>())
                        {
                            if (view.IsTemplate) continue;
                            if (scope.SelectedIndex == 1 && (!(view is View3D) || ((View3D)view).IsPerspective)) continue;
                            if (scope.SelectedIndex == 2 && view.ViewType != ViewType.FloorPlan) continue;
                            if (FilteredElementCollector.IsViewValidForElementIteration(document, view.Id))
                                items.Add(new TargetChoice(view.Id, view.Name + " [" + view.Id.IntegerValue + "]"));
                        }
                }
                items = items.OrderBy(i => i.Name).ToList();
                target.ItemsSource = items;
                target.SelectedItem = items.FirstOrDefault(i => i.Id.IntegerValue == document.ActiveView.Id.IntegerValue) ?? items.FirstOrDefault();
            }
            catch (Exception ex) { Status("Cannot load views: " + ex.Message); }
            finally { updating = false; }
            ClearScopeResults();
        }

        private void ClearScopeResults()
        {
            foreach (var item in categoryOptions)
            { if (item.IsAvailable) selections[item.Id] = item.IsSelected; item.PropertyChanged -= CategoryChanged; }
            categoryOptions = new List<CategoryOption>(); scanNotes = new List<string>(); categoriesLoaded = false;
            scopeResult = null; scheduleTables.Clear();
            SetCategoryView(); InvalidatePreview(); UpdateCategorySummary();
            previewButton.IsEnabled = scope.SelectedIndex >= 0 && (scope.SelectedIndex == 0 || TargetId != ElementId.InvalidElementId);
            Status("Choose a scope and view, then click Preview BOM to load categories.");
        }

        private void RefreshCategories()
        {
            InvalidatePreview();
            foreach (var item in categoryOptions)
            { if (item.IsAvailable) selections[item.Id] = item.IsSelected; item.PropertyChanged -= CategoryChanged; }
            categoryOptions = new List<CategoryOption>();
            scanNotes = new List<string>();
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (scope.SelectedIndex != 0 && TargetId == ElementId.InvalidElementId)
                    throw new InvalidOperationException("No view or Scope Box is available for this scope.");
                categoryOptions = BomService.GetAvailableCategories(document, (ExportScope)scope.SelectedIndex, TargetId, out scanNotes, Find<CheckBox>("IncludeLinksCheck").IsChecked == true);
                foreach (var item in categoryOptions)
                {
                    bool selected;
                    if (selections.TryGetValue(item.Id, out selected)) item.IsSelected = selected;
                    item.PropertyChanged += CategoryChanged;
                }
                categoriesLoaded = true;
                Status(categoryOptions.Count == 0 ? "No model elements found in this scope." :
                    categoryOptions.Count(c => c.IsAvailable) + " categories present · " + categoryOptions.Sum(c => c.Count) + " elements in scope.");
            }
            catch { categoriesLoaded = false; throw; }
            finally
            {
                SetCategoryView();
                notesButton.IsEnabled = scanNotes.Count > 0;
                UpdateCategorySummary();
                Mouse.OverrideCursor = null;
            }
        }

        private void SetCategoryView()
        {
            categoryView = CollectionViewSource.GetDefaultView(categoryOptions);
            categoryView.Filter = value =>
            {
                var category = (CategoryOption)value;
                return string.IsNullOrWhiteSpace(search.Text) || category.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
            };
            categories.ItemsSource = categoryView;
            bool loaded = categoryOptions.Count > 0;
            search.IsEnabled = loaded;
            foreach (string name in new[] { "MepButton", "AllButton", "NoneButton" }) Find<Button>(name).IsEnabled = loaded;
        }

        private void CategoryChanged(object sender, PropertyChangedEventArgs e)
        {
            var item = (CategoryOption)sender;
            selections[item.Id] = item.IsSelected;
            if (updating) return;
            RebuildPreview(); UpdateCategorySummary();
        }

        private void UpdateCategorySummary()
        {
            int visible = categoryView == null ? 0 : categoryView.Cast<CategoryOption>().Count();
            Find<TextBlock>("CategoryCountText").Text = categoriesLoaded ? categoryOptions.Count(c => c.IsAvailable) + " present / " + categoryOptions.Count(c => c.IsSelected) + " selected" : "";
            Find<TextBlock>("EmptyCategories").Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("EmptyCategories").Text = !categoriesLoaded ? "Click Preview BOM to load categories." : categoryOptions.Count == 0 ? "No categories in this scope." : "No categories match your search.";
        }

        private void SetSelection(string preset)
        {
            updating = true;
            try { foreach (var item in categoryOptions) item.IsSelected = item.IsAvailable && (preset == "All" || preset == "MEP" && BomService.DefaultCategories.Contains(item.Id)); }
            finally { updating = false; }
            RebuildPreview(); UpdateCategorySummary();
        }

        private void InvalidatePreview()
        {
            result = null; grid.ItemsSource = null; exportButton.IsEnabled = false;
            Find<Button>("HighlightButton").IsEnabled = false;
            Find<ComboBox>("ScheduleCategoryCombo").ItemsSource = null;
            Find<DataGrid>("ScheduleGrid").ItemsSource = null;
            Find<TextBlock>("SchedulePreviewStatus").Text = "Preview BOM first, then select a category. Native schedules include the entire host model.";
            Find<Button>("CreateScheduleButton").IsEnabled = false;
            Find<Button>("ModifyTitleButton").IsEnabled = false;
            foreach (string name in new[] { "BomQueryButton", "ScheduleTitleButton", "ScheduleQueryButton", "ExportSchedulesButton" }) Find<Button>(name).IsEnabled = false;
            notesButton.IsEnabled = scanNotes.Count > 0;
            Find<TextBlock>("ElementMetric").Text = "—"; Find<TextBlock>("RowMetric").Text = "—";
            Find<StackPanel>("EmptyPreview").Visibility = Visibility.Visible;
            Status("Selection changed. Click Preview BOM to refresh.");
        }

        private void Preview()
        {
            InvalidatePreview();
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (scope.SelectedIndex < 0) throw new InvalidOperationException("Choose an export scope first.");
                bool firstPreview = !categoriesLoaded;
                if (scopeResult != null) { RebuildPreview(); return; }
                RefreshCategories();
                if (!categoryOptions.Any(c => c.IsAvailable)) { Status("No model elements found in this scope."); return; }
                // An architectural-only scope should still produce a useful first preview.
                if (firstPreview && !categoryOptions.Any(c => c.IsSelected) && categoryOptions.Where(c => c.IsAvailable).All(c => !selections.ContainsKey(c.Id)))
                    SetSelection("All");
                scopeResult = BomService.Collect(document, (ExportScope)scope.SelectedIndex, TargetId,
                    categoryOptions.Where(c => c.IsAvailable).Select(c => new ElementId(c.Id)).ToList(), Find<CheckBox>("IncludeLinksCheck").IsChecked == true);
                if (Find<CheckBox>("IncludeLinksCheck").IsChecked == true && layout.Columns.All(c => c.Key != "Source")) layout.Columns.Insert(1, new BomColumn { Key = "Source", Header = "Source" });
                if (layout.Columns.Any(c => c.Key.StartsWith("param:", StringComparison.Ordinal)) || bomSettings.Filters.Any(f => f.Field.StartsWith("param:", StringComparison.Ordinal)) || bomSettings.Sorts.Any(s => s.Field.StartsWith("param:", StringComparison.Ordinal))) BomService.LoadCommonParameters(document, scopeResult);
                RebuildPreview();
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Preview failed", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RebuildPreview()
        {
            if (scopeResult == null) return;
            var selected = new HashSet<int>(categoryOptions.Where(c => c.IsSelected && c.IsAvailable).Select(c => c.Id));
            foreach (var element in scopeResult.Elements) {
                string method = measurementRules.FirstOrDefault(r => r.CategoryId == element.CategoryId)?.Method ?? "Auto";
                double value;
                if (element.Measures.TryGetValue(method, out value)) { element.Quantity = value; element.Unit = method == "Count" ? "pcs" : method == "Length" ? "m" : method == "Area" ? "m²" : method == "Volume" ? "m³" : element.AutoUnit ?? element.Unit; }
                else { element.Quantity = 0; element.Unit = method == "Length" ? "m" : method == "Area" ? "m²" : "m³"; }
            }
            result = new BomResult { Model = scopeResult.Model, Scope = scopeResult.Scope, Elements = scopeResult.Elements.Where(e => selected.Contains(e.CategoryId)).ToList(),
                CommonParameters = scopeResult.CommonParameters, Query = bomSettings.Clone(), Warnings = new List<string>(scopeResult.Warnings) };
            if (result.Elements.Count > 0 && scopeResult.ParameterFields.Count > 0) {
                var common = new HashSet<string>(result.Elements[0].ExtraValues.Keys);
                foreach (var element in result.Elements.Skip(1)) common.IntersectWith(element.ExtraValues.Keys);
                result.CommonParameters = scopeResult.ParameterFields.Where(f => common.Contains(f.Key)).ToList();
            }
            result.Rows = BomQuery.Apply(result.Elements, bomSettings);
            // Audit/export includes only instances contributing to the filtered table.
            var ids = new HashSet<string>(result.Rows.Where(r => !r.IsDecoration).SelectMany(r => r.RecordKeys));
            result.Elements = result.Elements.Where(e => ids.Contains(e.Identity)).ToList();
            result.TemplateMapping = templateMapping; result.TemplateTotalRow = templateTotalRow;
            result.Warnings.AddRange(StudioState.Quality(result.Elements));
            var quality = result.Elements.ToDictionary(e => e.Identity, e => StudioState.Quality(new[] { e }));
            foreach (var row in result.Rows.Where(r => !r.IsDecoration)) {
                row.Warnings = string.Join("\n", row.RecordKeys.Where(quality.ContainsKey).SelectMany(key => quality[key]));
                if (new[] { row.Source, row.Category, row.Family, row.Type, row.System, row.Size }.Contains("<varies>") || row.ExtraValues.Values.Contains("<varies>")) row.Warnings = (row.Warnings ?? "") + "\nGroup contains different values; review <varies> cells.";
            }
            if (result.Rows.Any(r => r.HasWarnings) && layout.Columns.All(c => c.Key != "Warnings")) layout.Columns.Add(new BomColumn { Key = "Warnings", Header = "Warnings" });
            foreach (var row in result.Rows.Where(r => r.ExtraValues.Values.Contains("<varies>"))) result.Warnings.Add("Mixed parameter values in BOM row " + row.No + ": review <varies> cells.");
            foreach (var element in result.Elements.Where(e => !e.Measures.ContainsKey(measurementRules.FirstOrDefault(r => r.CategoryId == e.CategoryId)?.Method ?? "Auto"))) result.Warnings.Add("Measurement unavailable: " + element.SourceName + " / " + element.ElementId + ". Quantity shown as zero; choose a supported measurement.");
            result.Warnings.AddRange(result.Rows.Where(r => r.HasWarnings).Select(r => r.Warnings)); result.Warnings = result.Warnings.Distinct().ToList();
            ApplyLayout();
            var combo = Find<ComboBox>("ScheduleCategoryCombo"); var previous = combo.SelectedItem as CategoryOption;
            updating = true;
            try { combo.ItemsSource = categoryOptions.Where(c => selected.Contains(c.Id) && !document.IsFamilyDocument && ViewSchedule.IsValidCategoryForSchedule(new ElementId(c.Id))).ToList();
                combo.SelectedItem = combo.Items.Cast<CategoryOption>().FirstOrDefault(c => previous != null && c.Id == previous.Id) ?? combo.Items.Cast<CategoryOption>().FirstOrDefault(); }
            finally { updating = false; }
            bool schedules = combo.Items.Count > 0;
            foreach (string name in new[] { "CreateScheduleButton", "ScheduleTitleButton", "ScheduleQueryButton", "ExportSchedulesButton" }) Find<Button>(name).IsEnabled = schedules;
            Find<Button>("ModifyTitleButton").IsEnabled = selected.Count > 0; Find<Button>("BomQueryButton").IsEnabled = selected.Count > 0;
            exportButton.IsEnabled = ids.Count > 0; notesButton.IsEnabled = true;
            Find<Button>("HighlightButton").IsEnabled = ids.Count > 0 && Find<TabControl>("PreviewTabs").SelectedIndex == 0;
            Find<TextBlock>("ElementMetric").Text = ids.Count.ToString("N0"); Find<TextBlock>("RowMetric").Text = result.Rows.Count(r => !r.ExtraValues.ContainsKey("__decoration")).ToString("N0");
            Find<StackPanel>("EmptyPreview").Visibility = ids.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("BomQuerySummary").Text = bomSettings.Filters.Count + " filters · " + bomSettings.Sorts.Count + " sort/group rules · " + (bomSettings.Itemized ? "Itemized" : "BOM groups");
            previewButton.IsEnabled = false;
            scheduleTables.Clear();
            Status("Preview updated · " + ids.Count + " elements · " + result.Scope);
            PreviewSchedule();
        }

        private void RequestSchedule()
        {
            var selected = categoryOptions.Where(c => c.IsSelected && c.IsAvailable && ViewSchedule.IsValidCategoryForSchedule(new ElementId(c.Id))).ToList();
            if (result == null || selected.Count == 0) return;
            var existing = new FilteredElementCollector(document).OfClass(typeof(RevitView)).Cast<RevitView>().Select(v => v.Name).ToList();
            var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            var requests = selected.Select(c => new ScheduleRequest { CategoryId = c.Id, CategoryName = c.Name,
                Supported = true, Options = GetScheduleOptions(c), ScheduleName = GetScheduleOptions(c).Title }).ToList();
            var dialog = ConfigurationDialogs.BuildNamesWindow(window, requests, existing);
            if (dialog.ShowDialog() != true) return;
            ScheduleRequests = requests.Where(r => r.Supported).ToList();
            // Revit view activation is performed after the modal window closes.
            window.Close();
        }

        private void PreviewSchedule()
        {
            if (updating || Find<TabControl>("PreviewTabs").SelectedIndex != 1) return;
            var category = Find<ComboBox>("ScheduleCategoryCombo").SelectedItem as CategoryOption;
            var scheduleGrid = Find<DataGrid>("ScheduleGrid");
            scheduleGrid.ItemsSource = null; scheduleGrid.Columns.Clear();
            if (result == null || category == null)
            { Find<TextBlock>("SchedulePreviewStatus").Text = "Preview BOM with a category that supports Revit schedules."; return; }
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var options = GetScheduleOptions(category);
                System.Data.DataTable table;
                if (!scheduleTables.TryGetValue(category.Id, out table)) { table = RevitScheduleService.Preview(document, category.Id, category.Name, options); scheduleTables[category.Id] = table; }
                var visibleFields = options.Fields.Where(f => f.Visible).ToList();
                for (int i = 0; i < table.Columns.Count; i++) {
                    var field = i < visibleFields.Count ? visibleFields[i] : null;
                    scheduleGrid.Columns.Add(new DataGridTextColumn { Header = field?.Heading ?? table.Columns[i].ColumnName,
                        Binding = new Binding("[" + table.Columns[i].ColumnName + "]"), Width = DataGridLength.Auto, MinWidth = 120, MaxWidth = 320, ElementStyle = window.FindResource("TableText") as Style,
                        SortMemberPath = field?.Key ?? "", CanUserSort = field != null,
                        SortDirection = field == null || !options.Settings.Sorts.Any(s => s.Field == field.Key) ? (ListSortDirection?)null : options.Settings.Sorts.First(s => s.Field == field.Key).Descending ? ListSortDirection.Descending : ListSortDirection.Ascending });
                }
                var display = table.Copy();
                if (display.Rows.Count > 0 && visibleFields.Count == table.Columns.Count && visibleFields.Select((f, i) => Convert.ToString(display.Rows[0][i]) == f.Heading).All(v => v)) display.Rows.RemoveAt(0);
                scheduleGrid.ItemsSource = display.DefaultView;
                Find<TextBlock>("SchedulePreviewStatus").Text = options.Title + " · " + (options.ScopeElementIds == null ? "Entire host model" : "BOM scope: " + options.ScopeElementIds.Count + " host elements (links excluded)") + " · " + options.Settings.Filters.Count + " filters · " + options.Settings.Sorts.Count + " sort/group rules";
            }
            catch (Exception ex) { Find<TextBlock>("SchedulePreviewStatus").Text = "Cannot preview schedule: " + ex.Message; }
            finally { Mouse.OverrideCursor = null; }
        }

        private void ModifyTitle()
        {
            if (result == null) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                BomService.LoadCommonParameters(document, scopeResult);
                RebuildPreview();
                ApplyLayout();
                Mouse.OverrideCursor = null;
                ConfigurationDialogs.BuildLayoutWindow(window, result.Layout, result.CommonParameters, accepted =>
                { layout = accepted; RebuildPreview(); Status("BOM title and columns updated."); }).ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Modify Title", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Mouse.OverrideCursor = null; }
        }

        private NativeScheduleOptions GetScheduleOptions(CategoryOption category)
        {
            NativeScheduleOptions options;
            if (!scheduleOptions.TryGetValue(category.Id, out options)) {
                options = RevitScheduleService.GetOptions(document, category.Id, category.Name);
                if (Find<CheckBox>("MatchScopeCheck").IsChecked == true) options.Title = "BOM - " + category.Name + " - Scope";
                scheduleOptions.Add(category.Id, options);
            }
            options.ScopeElementIds = Find<CheckBox>("MatchScopeCheck").IsChecked == true && result != null ? result.Elements.Where(e => e.CategoryId == category.Id && e.LinkInstanceId == 0).Select(e => e.ElementId).ToList() : null;
            return options;
        }
        private void ModifyScheduleTitle()
        {
            var category = Find<ComboBox>("ScheduleCategoryCombo").SelectedItem as CategoryOption; if (category == null) return;
            try {
                ConfigurationDialogs.BuildNativeLayoutWindow(window, GetScheduleOptions(category), accepted => {
                    // Validate native compatibility before accepting settings.
                    var table = RevitScheduleService.Preview(document, category.Id, category.Name, accepted);
                    scheduleOptions[category.Id] = accepted; scheduleTables[category.Id] = table; PreviewSchedule();
                }).ShowDialog();
            } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Revit schedule title/fields"); }
        }
        private void ConfigureBomQuery()
        {
            if (scopeResult == null) return;
            try {
                BomService.LoadCommonParameters(document, scopeResult); RebuildPreview();
                var fields = BomLayout.Defaults().Select(c => new ParameterField { Key = c.Key, Name = c.Header }).Concat(new[] { new ParameterField { Key = "Source", Name = "Source" } }).Concat(result.CommonParameters).ToList();
                ConfigurationDialogs.BuildQueryWindow(window, bomSettings, fields, false, accepted => { bomSettings = accepted; RebuildPreview(); }, BomFilterValues).ShowDialog();
            } catch (Exception ex) { MessageBox.Show(window, ex.Message, "BOM filters/sorting"); }
        }
        private void ConfigureScheduleQuery()
        {
            var category = Find<ComboBox>("ScheduleCategoryCombo").SelectedItem as CategoryOption; if (category == null) return;
            try {
                var options = GetScheduleOptions(category);
                ConfigurationDialogs.BuildQueryWindow(window, options.Settings, options.Fields.Select(f => new ParameterField { Key = f.Key, Name = f.Heading }).ToList(), true, accepted => {
                    var working = options.Clone(); working.Settings = accepted;
                    var table = RevitScheduleService.Preview(document, category.Id, category.Name, working);
                    scheduleOptions[category.Id] = working; scheduleTables[category.Id] = table; PreviewSchedule();
                }, field => RevitScheduleService.FilterValues(document, category.Id, field, options.ScopeElementIds)).ShowDialog();
            } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Schedule filters/sorting"); }
        }
        private void ExportSchedules()
        {
            try {
                var selected = categoryOptions.Where(c => c.IsAvailable && c.IsSelected && !document.IsFamilyDocument && ViewSchedule.IsValidCategoryForSchedule(new ElementId(c.Id))).ToList();
                var requests = selected.Select(c => new ScheduleRequest { CategoryId = c.Id, CategoryName = c.Name, Options = GetScheduleOptions(c), ScheduleName = GetScheduleOptions(c).Title }).ToList();
                if (requests.Count == 0) return;
                if (ConfigurationDialogs.BuildScheduleSelection(window, requests).ShowDialog() != true) return;
                var save = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", FileName = "Revit_Schedules_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx" };
                if (save.ShowDialog(window) != true) return;
                Mouse.OverrideCursor = Cursors.Wait;
                var tables = new Dictionary<string, List<object[]>>();
                foreach (var request in requests.Where(r => r.IsSelected)) {
                    System.Data.DataTable table;
                    if (!scheduleTables.TryGetValue(request.CategoryId, out table)) { table = RevitScheduleService.Preview(document, request.CategoryId, request.CategoryName, request.Options); scheduleTables[request.CategoryId] = table; }
                    var title = request.ScheduleName; int suffix = 2; while (tables.ContainsKey(title)) title = request.ScheduleName + " (" + suffix++ + ")";
                    tables.Add(title, table.Rows.Cast<System.Data.DataRow>().Select(r => r.ItemArray).ToList());
                }
                ExcelExporter.ExportSchedules(save.FileName, tables); Status("Exported " + tables.Count + " schedule sheets: " + save.FileName);
            } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Schedule export"); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void ApplyLayout()
        {
            if (result == null) return;
            result.Layout = layout.Clone();
            var common = new HashSet<string>(result.CommonParameters.Select(p => p.Key));
            foreach (var column in result.Layout.Columns.Where(c => c.Key.StartsWith("param:", StringComparison.Ordinal) && !common.Contains(c.Key)).ToList())
            { result.Layout.Columns.Remove(column); result.Warnings.Add("Column is not common to the current scope and was omitted: " + column.Header); }
            result.Layout.Validate();
            grid.Columns.Clear();
            foreach (var column in result.Layout.Columns.Where(c => c.IsVisible))
            {
                var path = column.Key == "No" ? "DisplayNo" : column.Key == "Quantity" ? "DisplayQuantity" : column.Key == "ElementCount" ? "DisplayElementCount" : column.Key;
                var binding = new Binding(column.Key.StartsWith("param:", StringComparison.Ordinal) ? "ExtraValues[" + column.Key + "]" : path);
                if (column.Key == "Quantity") binding.StringFormat = "N3";
                grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = binding,
                    SortMemberPath = column.Key, MinWidth = column.Key == "No" ? 50 : column.Key == "Unit" ? 70 : column.Key == "ElementCount" ? 130 : 120,
                    SortDirection = !bomSettings.Sorts.Any(s => s.Field == column.Key) ? (ListSortDirection?)null : bomSettings.Sorts.First(s => s.Field == column.Key).Descending ? ListSortDirection.Descending : ListSortDirection.Ascending,
                    ElementStyle = window.FindResource("TableText") as Style,
                    Width = column.Key == "No" ? new DataGridLength(50) : column.Key == "Unit" ? new DataGridLength(70) : column.Key == "ElementIds" ? new DataGridLength(230) : new DataGridLength(160) });
            }
            Find<TextBlock>("PreviewTitleText").Text = result.Layout.Title;
            grid.ItemsSource = result.Rows;
        }

        private void UpdateTemplateControls()
        {
            bool enabled = !string.IsNullOrWhiteSpace(template.Text);
            sheet.IsEnabled = enabled; dataRow.IsEnabled = enabled;
            Find<Button>("ClearTemplateButton").IsEnabled = enabled;
        }

        private void Export()
        {
            if (result == null) return;
            int start = 6;
            if (!string.IsNullOrWhiteSpace(template.Text) && (!int.TryParse(dataRow.Text, out start) || start < 1 || start > 1048576))
            { MessageBox.Show(window, "Data row must be an integer between 1 and 1,048,576.", "Excel output"); return; }
            var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx",
                FileName = "BOM_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx" };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                ExcelExporter.Export(dialog.FileName, result, template.Text, sheet.Text.Trim(), start, Find<CheckBox>("CategorySheetsCheck").IsChecked == true);
                try { StudioState.Save(Path.ChangeExtension(dialog.FileName, "bom.xml"), Snapshot()); }
                catch (Exception ex) { MessageBox.Show(window, "Excel was saved, but snapshot could not be saved: " + ex.Message, "BOM snapshot"); }
                Status("Exported: " + dialog.FileName);
                MessageBox.Show(window, "Excel saved successfully.\n" + dialog.FileName, "Export BOM", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void ShowNotes()
        {
            var notes = result == null ? scanNotes : result.Warnings;
            var dialog = new WpfWindow { Title = "BOM notes / warnings", Owner = window, Width = 840, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = window.Background,
                Content = new TextBox { Text = string.Join(Environment.NewLine, notes), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), Padding = new Thickness(12) } };
            dialog.ShowDialog();
        }

        private void Status(string text) { Find<TextBlock>("StatusText").Text = text; }

        private sealed class TargetChoice
        {
            public TargetChoice(ElementId id, string name) { Id = id; Name = name; }
            public ElementId Id { get; private set; }
            public string Name { get; private set; }
        }
    }
}
