using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
            foreach (string state in new[] { "empty", "preview", "minimum" })
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
                    new CategoryOption { Id = 7, Name = "Cable Trays", Count = 40 }
                };
                if (state != "empty")
                {
                    Find<ListBox>(window, "CategoryList").ItemsSource = categories;
                    Find<TextBlock>(window, "CategoryCountText").Text = "7 present / 6 selected";
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
                    Find<Button>(window, "CreateScheduleButton").IsEnabled = true;
                    Find<Button>(window, "ModifyTitleButton").IsEnabled = true;
                }
                else Find<TextBlock>(window, "StatusText").Text = "Choose a scope and view, then click Preview BOM to load categories.";
                var content = (FrameworkElement)window.Content;
                double width = state == "minimum" ? 1060 : 1220;
                double height = state == "minimum" ? 820 : 860;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                var render = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); render.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (var output = File.Create(Path.Combine(folder, state + ".png"))) encoder.Save(output);
                var check = Find<ListBox>(window, "CategoryList");
                int expected = state == "empty" ? 0 : 7;
                if (check.Items.Count != expected || check.ActualHeight < 80 || Find<Button>(window, "ExportButton").ActualWidth < 100)
                    throw new Exception("Layout is clipped: " + state);
                if (state == "empty" && (Find<ComboBox>(window, "ScopeCombo").SelectedIndex != -1 || Find<ComboBox>(window, "TargetCombo").Items.Count != 0 || Find<Button>(window, "CreateScheduleButton").IsEnabled))
                    throw new Exception("Initial state contains preloaded selections.");
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
                    Capture(names, folder, "schedule-names", 900, 560); names.Close();
                    var titles = ConfigurationDialogs.BuildLayoutWindow(window, new BomLayout(), new List<ParameterField>
                    { new ParameterField { Key = "param:1", Name = "Comments" }, new ParameterField { Key = "param:2", Name = "Manufacturer" } }, accepted => { });
                    Capture(titles, folder, "modify-title", 1020, 680); titles.Close();
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
}
