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
    internal sealed class ExportBomWindow
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
        private bool updating;
        private bool categoriesLoaded;
        private BomLayout layout = new BomLayout();
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
            exportButton.Click += (s, e) => Export();
            notesButton.Click += (s, e) => ShowNotes();
            Find<Button>("CreateScheduleButton").Click += (s, e) => RequestSchedule();
            Find<Button>("ModifyTitleButton").Click += (s, e) => ModifyTitle();
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
            { selections[item.Id] = item.IsSelected; item.PropertyChanged -= CategoryChanged; }
            categoryOptions = new List<CategoryOption>(); scanNotes = new List<string>(); categoriesLoaded = false;
            SetCategoryView(); InvalidatePreview(); UpdateCategorySummary();
            previewButton.IsEnabled = scope.SelectedIndex >= 0 && (scope.SelectedIndex == 0 || TargetId != ElementId.InvalidElementId);
            Status("Choose a scope and view, then click Preview BOM to load categories.");
        }

        private void RefreshCategories()
        {
            InvalidatePreview();
            foreach (var item in categoryOptions)
            { selections[item.Id] = item.IsSelected; item.PropertyChanged -= CategoryChanged; }
            categoryOptions = new List<CategoryOption>();
            scanNotes = new List<string>();
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (scope.SelectedIndex != 0 && TargetId == ElementId.InvalidElementId)
                    throw new InvalidOperationException("No view or Scope Box is available for this scope.");
                categoryOptions = BomService.GetAvailableCategories(document, (ExportScope)scope.SelectedIndex, TargetId, out scanNotes);
                foreach (var item in categoryOptions)
                {
                    bool selected;
                    if (selections.TryGetValue(item.Id, out selected)) item.IsSelected = selected;
                    item.PropertyChanged += CategoryChanged;
                }
                categoriesLoaded = true;
                Status(categoryOptions.Count == 0 ? "No model elements found in this scope." :
                    categoryOptions.Count + " categories present · " + categoryOptions.Sum(c => c.Count) + " elements in scope. Choose categories, then Preview BOM.");
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
            InvalidatePreview(); UpdateCategorySummary();
        }

        private void UpdateCategorySummary()
        {
            int visible = categoryView == null ? 0 : categoryView.Cast<CategoryOption>().Count();
            Find<TextBlock>("CategoryCountText").Text = categoriesLoaded ? categoryOptions.Count + " present / " + categoryOptions.Count(c => c.IsSelected) + " selected" : "";
            Find<TextBlock>("EmptyCategories").Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("EmptyCategories").Text = !categoriesLoaded ? "Click Preview BOM to load categories." : categoryOptions.Count == 0 ? "No categories in this scope." : "No categories match your search.";
        }

        private void SetSelection(string preset)
        {
            updating = true;
            try { foreach (var item in categoryOptions) item.IsSelected = preset == "All" || preset == "MEP" && BomService.DefaultCategories.Contains(item.Id); }
            finally { updating = false; }
            InvalidatePreview(); UpdateCategorySummary();
        }

        private void InvalidatePreview()
        {
            result = null; grid.ItemsSource = null; exportButton.IsEnabled = false;
            Find<Button>("CreateScheduleButton").IsEnabled = false;
            Find<Button>("ModifyTitleButton").IsEnabled = false;
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
                if (firstPreview) RefreshCategories();
                if (categoryOptions.Count == 0) { Status("No model elements found in this scope."); return; }
                // An architectural-only scope should still produce a useful first preview.
                if (firstPreview && !categoryOptions.Any(c => c.IsSelected) && categoryOptions.All(c => !selections.ContainsKey(c.Id)))
                    SetSelection("All");
                result = BomService.Collect(document, (ExportScope)scope.SelectedIndex, TargetId,
                    categoryOptions.Where(c => c.IsSelected).Select(c => new ElementId(c.Id)).ToList());
                if (layout.Columns.Any(c => c.Key.StartsWith("param:", StringComparison.Ordinal))) BomService.LoadCommonParameters(document, result);
                ApplyLayout();
                Find<TextBlock>("ElementMetric").Text = result.Elements.Count.ToString("N0");
                Find<TextBlock>("RowMetric").Text = result.Rows.Count.ToString("N0");
                Find<StackPanel>("EmptyPreview").Visibility = result.Rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                exportButton.IsEnabled = result.Rows.Count > 0; notesButton.IsEnabled = true;
                var selected = categoryOptions.Where(c => c.IsSelected).ToList();
                Find<Button>("CreateScheduleButton").IsEnabled = result.Rows.Count > 0 && !document.IsFamilyDocument &&
                    selected.Any(c => ViewSchedule.IsValidCategoryForSchedule(new ElementId(c.Id)));
                Find<Button>("ModifyTitleButton").IsEnabled = result.Rows.Count > 0;
                Status(result.Elements.Count + " elements / " + result.Rows.Count + " BOM rows · " + result.Scope);
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Preview failed", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RequestSchedule()
        {
            var selected = categoryOptions.Where(c => c.IsSelected).ToList();
            if (result == null || selected.Count == 0) return;
            var existing = new FilteredElementCollector(document).OfClass(typeof(RevitView)).Cast<RevitView>().Select(v => v.Name).ToList();
            var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            var requests = selected.Select(c => new ScheduleRequest { CategoryId = c.Id, CategoryName = c.Name,
                Supported = ViewSchedule.IsValidCategoryForSchedule(new ElementId(c.Id)), ScheduleName = ScheduleNames.Suggest(c.Name, used) }).ToList();
            var dialog = ConfigurationDialogs.BuildNamesWindow(window, requests, existing);
            if (dialog.ShowDialog() != true) return;
            ScheduleRequests = requests.Where(r => r.Supported).ToList();
            // Revit view activation is performed after the modal window closes.
            window.Close();
        }

        private void ModifyTitle()
        {
            if (result == null) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                BomService.LoadCommonParameters(document, result);
                ApplyLayout();
                Mouse.OverrideCursor = null;
                ConfigurationDialogs.BuildLayoutWindow(window, result.Layout, result.CommonParameters, accepted =>
                { layout = accepted; ApplyLayout(); Status("Title and columns updated. Required common fields remain visible."); }).ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Modify Title", MessageBoxButton.OK, MessageBoxImage.Error); }
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
                var binding = new Binding(column.Key.StartsWith("param:", StringComparison.Ordinal) ? "ExtraValues[" + column.Key + "]" : column.Key);
                if (column.Key == "Quantity") binding.StringFormat = "N3";
                grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = binding,
                    Width = column.Key == "No" ? 45 : column.Key == "Unit" ? 65 : column.Key == "Quantity" || column.Key == "ElementCount" ? 100 : 135 });
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
