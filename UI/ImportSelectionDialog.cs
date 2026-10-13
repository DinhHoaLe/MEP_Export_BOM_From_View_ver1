using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MEP_Export_BOM_From_View_ver1
{
    internal static partial class ConfigurationDialogs
    {
        internal static Window BuildImportWindow(Window owner, IList<ImportSheet> sheets, IList<ImportField> fields,
            IList<ElementRecord> elements, Action<ImportMatchResult, string> accepted)
        {
            var dialog = Base(owner, "Import Excel / CSV element selection", 1000, 680);
            var layout = Rows(dialog, "Match elements using one key column", "Choose a worksheet, header row, source key column and target parameter/title. Match searches the current scope; values are read only. Text matching ignores case and outer spaces. Numeric values without units use the parameter's displayed units.");
            var body = new System.Windows.Controls.Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var worksheet = new ComboBox { ItemsSource = sheets, DisplayMemberPath = "Name", Width = 240, Margin = new Thickness(8, 0, 18, 0) };
            var header = new TextBox { Width = 65, Margin = new Thickness(8, 0, 16, 0) };
            var reload = Button(dialog, "Read headings", false);
            controls.Children.Add(new TextBlock { Text = "Sheet", VerticalAlignment = VerticalAlignment.Center }); controls.Children.Add(worksheet);
            controls.Children.Add(new TextBlock { Text = "Header row", VerticalAlignment = VerticalAlignment.Center }); controls.Children.Add(header); controls.Children.Add(reload);
            body.Children.Add(controls);
            var keyControls = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var source = new ComboBox { Width = 260, DisplayMemberPath = "Name", Margin = new Thickness(8, 0, 16, 0) };
            var target = new ComboBox { Width = 340, ItemsSource = fields, DisplayMemberPath = "Name", Margin = new Thickness(8, 0, 16, 0) };
            var match = Button(dialog, "Match / Preview", false);
            keyControls.Children.Add(new TextBlock { Text = "Key column", VerticalAlignment = VerticalAlignment.Center }); keyControls.Children.Add(source);
            keyControls.Children.Add(new TextBlock { Text = "Target", VerticalAlignment = VerticalAlignment.Center }); keyControls.Children.Add(target); keyControls.Children.Add(match);
            System.Windows.Controls.Grid.SetRow(keyControls, 1); body.Children.Add(keyControls);
            var table = Grid(true); table.Columns.Add(new DataGridTextColumn { Header = "File row", Binding = new Binding("Row"), Width = 85 });
            table.Columns.Add(new DataGridTextColumn { Header = "Key value", Binding = new Binding("Value"), Width = 460 });
            table.Columns.Add(new DataGridTextColumn { Header = "Matched elements", Binding = new Binding("Matches"), Width = 160 });
            table.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 180 });
            System.Windows.Controls.Grid.SetRow(table, 2); body.Children.Add(table);
            System.Windows.Controls.Grid.SetRow(body, 2); layout.Children.Add(body);
            var actions = Actions(layout);
            var summary = new TextBlock { Text = "Choose a key and click Match / Preview.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close();
            var apply = Button(dialog, "Apply selection", true); apply.IsEnabled = false;
            actions.Children.Add(summary); actions.Children.Add(cancel); actions.Children.Add(apply);
            ImportMatchResult preview = null;
            string label = "";
            Action invalidate = () => { preview = null; table.ItemsSource = null; apply.IsEnabled = false; summary.Text = "Click Match / Preview after changing the key."; };
            Action readHeadings = () =>
            {
                invalidate();
                var chosen = worksheet.SelectedItem as ImportSheet; int row;
                if (chosen == null || !int.TryParse(header.Text, out row) || row < 1) { summary.Text = "Enter a valid header row."; return; }
                var heading = chosen.Rows.FirstOrDefault(r => r.Number == row);
                if (heading == null) { source.ItemsSource = null; summary.Text = "Header row not found."; return; }
                source.ItemsSource = heading.Cells.Select((name, index) => new ImportColumn { Index = index, Name = name.Trim(), Heading = name.Trim() }).Where(c => c.Name.Length > 0).ToList();
                var columns = source.Items.Cast<ImportColumn>().ToList();
                source.SelectedItem = columns.FirstOrDefault(c => ImportSelection.Resolve(c.Heading, fields)?.Key == "UniqueId") ??
                    columns.FirstOrDefault(c => ImportSelection.Resolve(c.Heading, fields)?.Key == "ElementId") ??
                    columns.FirstOrDefault(c => ImportSelection.Resolve(c.Heading, fields) != null) ?? columns.FirstOrDefault();
            };
            worksheet.SelectionChanged += (s, e) => { var chosen = worksheet.SelectedItem as ImportSheet; if (chosen == null) return; header.Text = ImportSelection.GuessHeader(chosen, fields).ToString(); readHeadings(); };
            header.TextChanged += (s, e) => { invalidate(); source.ItemsSource = null; target.SelectedItem = null; };
            reload.Click += (s, e) => readHeadings();
            source.SelectionChanged += (s, e) => { invalidate(); var column = source.SelectedItem as ImportColumn; target.SelectedItem = column == null ? null : ImportSelection.Resolve(column.Heading, fields); };
            target.SelectionChanged += (s, e) => invalidate();
            match.Click += (s, e) =>
            {
                try
                {
                    int row; if (!int.TryParse(header.Text, out row)) throw new InvalidOperationException("Enter the header row.");
                    var column = source.SelectedItem as ImportColumn; var field = target.SelectedItem as ImportField;
                    if (column == null) throw new InvalidOperationException("Choose a source key column.");
                    preview = ImportSelection.Match((ImportSheet)worksheet.SelectedItem, row, column.Index, field, elements);
                    table.ItemsSource = preview.Rows;
                    summary.Text = preview.Identities.Count + " elements · " + preview.Rows.Count(r => r.Matches == 0) + " unmatched rows";
                    label = ((ImportSheet)worksheet.SelectedItem).Name + " / " + column.Heading + " → " + field.Name;
                    apply.IsEnabled = true; // A zero-match result is explicit and must stay empty, never revert to all elements.
                }
                catch (Exception ex) { invalidate(); summary.Text = ex.Message; }
            };
            apply.Click += (s, e) => { if (preview == null) return; accepted(preview, label); dialog.DialogResult = true; };
            worksheet.SelectedIndex = 0;
            return dialog;
        }
        private sealed class ImportColumn
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Heading { get; set; }
        }
    }
}
