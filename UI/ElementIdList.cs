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
        internal static DataGridTemplateColumn ElementIdColumn(Window owner)
        {
            var button = new FrameworkElementFactory(typeof(Button), "ViewIdsButton");
            button.SetValue(System.Windows.Controls.Button.ContentProperty, "View"); button.SetValue(FrameworkElement.StyleProperty, owner.FindResource("ChipButton"));
            button.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4));
            button.SetValue(FrameworkElement.ToolTipProperty, "View, copy and locate the element IDs in this BOM row.");
            button.SetBinding(FrameworkElement.TagProperty, new Binding());
            var template = new DataTemplate { VisualTree = button };
            var hide = new DataTrigger { Binding = new Binding("IsDecoration"), Value = true };
            hide.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ViewIdsButton")); template.Triggers.Add(hide);
            return new DataGridTemplateColumn { Header = "Element IDs", CellTemplate = template, Width = 110, MinWidth = 100, CanUserSort = false };
        }

        internal static Window BuildElementIdsWindow(Window owner, IList<ElementRecord> records,
            Action<ElementRecord> properties, Action<IList<ElementRecord>> view)
        {
            var dialog = Base(owner, "BOM Element IDs", 1040, 650);
            var layout = Rows(dialog, "Element IDs · " + records.Count + " instance(s)", "Select one or more rows to copy IDs or inspect Properties. View in Revit closes this tool, then selects and zooms the chosen instances. Source distinguishes host and linked IDs.");
            var table = Grid(true); table.SelectionMode = DataGridSelectionMode.Extended;
            table.ItemsSource = records.OrderBy(r => r.SourceName).ThenBy(r => r.ElementId).ToList();
            table.Columns.Add(new DataGridTextColumn { Header = "Element ID", Binding = new Binding("ElementId"), Width = 110 });
            table.Columns.Add(new DataGridTextColumn { Header = "Source", Binding = new Binding("SourceName"), Width = 230 });
            table.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("Category"), Width = 150 });
            table.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("Type"), Width = 190 });
            table.Columns.Add(new DataGridTextColumn { Header = "Link instance ID", Binding = new Binding("LinkInstanceId"), Width = 140 });
            table.Columns.Add(new DataGridTextColumn { Header = "Unique ID", Binding = new Binding("UniqueId"), Width = 290 });
            System.Windows.Controls.Grid.SetRow(table, 2); layout.Children.Add(table);
            var actions = Actions(layout);
            Action<IList<ElementRecord>> copy = items =>
            {
                if (items.Count == 0) { MessageBox.Show(dialog, "Select at least one element ID.", "Copy IDs"); return; }
                try { Clipboard.SetText(string.Join(Environment.NewLine, items.Select(r => r.ElementId))); }
                catch (Exception ex) { MessageBox.Show(dialog, "Cannot copy IDs: " + ex.Message, "Clipboard"); }
            };
            var copySelected = Button(dialog, "Copy selected IDs", false); copySelected.Click += (s, e) => copy(table.SelectedItems.Cast<ElementRecord>().ToList());
            var copyAll = Button(dialog, "Copy all IDs", false); copyAll.Click += (s, e) => copy(records);
            var inspect = Button(dialog, "Properties", false); inspect.Click += (s, e) => { var record = table.SelectedItem as ElementRecord; if (record != null) properties(record); };
            var show = Button(dialog, "View in Revit / Close", true); show.Click += (s, e) =>
            {
                var selected = table.SelectedItems.Cast<ElementRecord>().ToList();
                if (selected.Count == 0) { MessageBox.Show(dialog, "Select at least one element.", "View in Revit"); return; }
                dialog.Close(); view(selected);
            };
            var close = Button(dialog, "Close", false); close.Click += (s, e) => dialog.Close();
            foreach (var action in new[] { copySelected, copyAll, inspect, show, close }) actions.Children.Add(action);
            table.MouseDoubleClick += (s, e) => { var row = ItemsControl.ContainerFromElement(table, e.OriginalSource as DependencyObject) as DataGridRow; var record = row?.Item as ElementRecord; if (record != null) properties(record); };
            table.SelectedIndex = 0;
            return dialog;
        }
    }
}
