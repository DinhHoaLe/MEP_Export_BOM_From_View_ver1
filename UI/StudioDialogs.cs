using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MEP_Export_BOM_From_View_ver1
{
    internal static partial class ConfigurationDialogs
    {
        internal static Window BuildMeasurements(Window owner, IList<MeasurementRule> rules, Action<List<MeasurementRule>> accepted)
        {
            var working = rules.Select(r => new MeasurementRule { Category = r.Category, CategoryId = r.CategoryId, Method = r.Method }).ToList();
            var dialog = Base(owner, "Measurement rules", 800, 600);
            var layout = Rows(dialog, "Measurement by category", "Auto: MEP curves use length, other instances use Count. Choose Length (m), Area (m²) or Volume (m³) only where Revit has that quantity. Missing quantities are shown as zero and flagged in Notes.");
            var grid = Grid(false); grid.ItemsSource = working;
            grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("Category"), IsReadOnly = true, Width = 400 });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "Measure", ItemsSource = new[] { "Auto", "Count", "Length", "Area", "Volume" }, SelectedItemBinding = new Binding("Method") { Mode = BindingMode.TwoWay }, Width = 200 });
            layout.Children.Add(grid); System.Windows.Controls.Grid.SetRow(grid, 2);
            var actions = Actions(layout); var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close(); actions.Children.Add(cancel);
            var apply = Button(dialog, "Apply", true); actions.Children.Add(apply); apply.Click += (s, e) => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); accepted(working); dialog.DialogResult = true; };
            return dialog;
        }
        internal static Window BuildMapping(Window owner, IList<BomColumn> columns, IList<TemplateMapping> original, int totalRow, Action<List<TemplateMapping>, int> accepted)
        {
            var mappings = columns.Where(c => c.IsVisible).Select((c, i) => new TemplateMapping { Field = c.Key, Heading = c.Header, Column = original.FirstOrDefault(m => m.Field == c.Key)?.Column ?? ExcelColumn(i + 1) }).ToList();
            var dialog = Base(owner, "Excel template mapping", 820, 660);
            var layout = Rows(dialog, "Map fields to template columns", "Sheet and Data row are set on the main window. Header goes in the row above data. Only mapped columns are written; other columns/formulas remain. Optional total row must be below data and use a single unit.");
            var body = new DockPanel(); var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(new TextBlock { Text = "Total row (0 = none)", VerticalAlignment = VerticalAlignment.Center }); var total = new TextBox { Text = totalRow.ToString(), Width = 100, Margin = new Thickness(10, 0, 0, 0) }; panel.Children.Add(total); DockPanel.SetDock(panel, Dock.Bottom); body.Children.Add(panel);
            var grid = Grid(false); grid.ItemsSource = mappings;
            grid.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Heading"), IsReadOnly = true, Width = 430 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Excel column", Binding = new Binding("Column") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 180 }); body.Children.Add(grid);
            layout.Children.Add(body); System.Windows.Controls.Grid.SetRow(body, 2);
            var actions = Actions(layout); var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close(); actions.Children.Add(cancel);
            var reset = Button(dialog, "Default A:J", false); reset.Click += (s, e) => { accepted(new List<TemplateMapping>(), 0); dialog.DialogResult = true; }; actions.Children.Add(reset);
            var apply = Button(dialog, "Apply", true); actions.Children.Add(apply);
            apply.Click += (s, e) => { try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); var indices = mappings.Select(m => StudioState.ColumnNumber(m.Column)).ToList(); if (indices.Distinct().Count() != indices.Count) throw new InvalidOperationException("Choose a different target column for each field."); int row; if (!int.TryParse(total.Text, out row) || row < 0 || row > 1048576) throw new InvalidOperationException("Invalid total row."); accepted(mappings, row); dialog.DialogResult = true; } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Template mapping"); } };
            return dialog;
        }
        private static string ExcelColumn(int number) { string text = ""; while (number > 0) { number--; text = (char)('A' + number % 26) + text; number /= 26; } return text; }
        internal static Window BuildChanges(Window owner, IList<BomChange> changes, Action export)
        {
            var dialog = Base(owner, "Compare BOM snapshots", 1100, 680); var layout = Rows(dialog, "BOM changes", changes.Count + " changes · Added, Removed and Changed are compared by source + element UniqueId. Unit changes are not subtracted.");
            var grid = Grid(true); grid.ItemsSource = changes;
            foreach (var field in new[] { "Change", "Source", "Element", "Category", "Before", "After", "Unit", "Delta" }) grid.Columns.Add(new DataGridTextColumn { Header = field, Binding = new Binding(field), Width = field == "Before" || field == "After" ? 280 : 120 });
            layout.Children.Add(grid); System.Windows.Controls.Grid.SetRow(grid, 2); var actions = Actions(layout); var close = Button(dialog, "Close", false); close.Click += (s, e) => dialog.Close(); actions.Children.Add(close); var save = Button(dialog, "Export changes", true); save.Click += (s, e) => { try { export(); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Change export"); } }; actions.Children.Add(save); return dialog;
        }
    }
}
