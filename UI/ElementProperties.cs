using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MEP_Export_BOM_From_View_ver1
{
    internal static partial class ConfigurationDialogs
    {
        internal static Window BuildPropertiesWindow(Window owner, IList<ElementRecord> records, Func<ElementRecord, IList<ElementProperty>> read)
        {
            var dialog = Base(owner, "Element Properties", 1240, 720);
            var layout = Rows(dialog, "Element properties", "This BOM row contains " + records.Count + " instance(s). Choose an element to inspect its instance and type parameters. Values are read only.");
            var body = new System.Windows.Controls.Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
            var picker = new ComboBox { ItemsSource = records.Select(r => new PropertyChoice { Record = r, Caption = r.SourceName + " / ID " + r.ElementId + " / " + r.Family + ": " + r.Type }).ToList(), DisplayMemberPath = "Caption", Margin = new Thickness(0, 0, 0, 12) };
            body.Children.Add(picker);
            var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var tabs = new TabControl { Width = 255, Margin = new Thickness(0, 0, 16, 0) };
            tabs.Items.Add(new TabItem { Header = "All" }); tabs.Items.Add(new TabItem { Header = "Instance" }); tabs.Items.Add(new TabItem { Header = "Type" });
            controls.Children.Add(tabs);
            var search = new TextBox { Tag = "Search parameter name or value…" }; controls.Children.Add(search);
            System.Windows.Controls.Grid.SetRow(controls, 1); body.Children.Add(controls);
            var table = Grid(true); table.SelectionMode = DataGridSelectionMode.Extended;
            table.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding("Group"), Width = 110 });
            table.Columns.Add(new DataGridTextColumn { Header = "Property / parameter", Binding = new Binding("Name"), Width = 270 });
            table.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value"), Width = 395, ElementStyle = owner.FindResource("TableText") as Style });
            table.Columns.Add(new DataGridTextColumn { Header = "Access", Binding = new Binding("Access"), Width = 100 });
            System.Windows.Controls.Grid.SetRow(table, 2); body.Children.Add(table);
            System.Windows.Controls.Grid.SetRow(body, 2); layout.Children.Add(body);
            var actions = Actions(layout);
            var hint = new TextBlock { Text = "Select rows and press Ctrl+C to copy.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) }; actions.Children.Add(hint);
            var close = Button(dialog, "Close", true); close.Click += (s, e) => dialog.Close(); actions.Children.Add(close);
            var cache = new Dictionary<string, IList<ElementProperty>>();
            IList<ElementProperty> properties = new List<ElementProperty>();
            Action filter = () =>
            {
                string text = search.Text.Trim(); string group = tabs.SelectedIndex == 1 ? "Instance" : tabs.SelectedIndex == 2 ? "Type" : null;
                table.ItemsSource = properties.Where(p => (group == null || p.Group == group) && (text.Length == 0 ||
                    (p.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || (p.Value ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            };
            picker.SelectionChanged += (s, e) =>
            {
                var choice = picker.SelectedItem as PropertyChoice; if (choice == null) return;
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    if (!cache.TryGetValue(choice.Record.Identity, out properties)) cache[choice.Record.Identity] = properties = read(choice.Record);
                    filter();
                }
                catch (Exception ex) { properties = new List<ElementProperty>(); filter(); MessageBox.Show(dialog, ex.Message, "Cannot read properties"); }
                finally { Mouse.OverrideCursor = null; }
            };
            search.TextChanged += (s, e) => filter(); tabs.SelectionChanged += (s, e) => filter();
            picker.SelectedIndex = 0;
            return dialog;
        }
        private sealed class PropertyChoice
        {
            public ElementRecord Record { get; set; }
            public string Caption { get; set; }
        }
    }
}
