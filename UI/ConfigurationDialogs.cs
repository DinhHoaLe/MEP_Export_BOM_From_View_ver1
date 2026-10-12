using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MEP_Export_BOM_From_View_ver1
{
    internal static class ConfigurationDialogs
    {
        internal static Window BuildNamesWindow(Window owner, IList<ScheduleRequest> requests, IEnumerable<string> existingNames)
        {
            var dialog = Base(owner, "Create Revit Schedules", 900, 560);
            var layout = Rows(dialog, "Name each category schedule", "Creates one schedule per supported category. Native schedules use the entire model; field settings remain at their defaults.");
            var grid = Grid(false);
            grid.ItemsSource = requests;
            grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("CategoryName"), IsReadOnly = true, Width = 210 });
            var nameStyle = new Style(typeof(TextBox));
            nameStyle.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding("Supported")));
            grid.Columns.Add(new DataGridTextColumn { Header = "Schedule name", Binding = new Binding("ScheduleName") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 450, EditingElementStyle = nameStyle });
            grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), IsReadOnly = true, Width = 160 });
            layout.Children.Add(grid); System.Windows.Controls.Grid.SetRow(grid, 2);
            var actions = Actions(layout);
            var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close();
            var create = Button(dialog, "Create schedules", true);
            create.Click += (s, e) =>
            {
                try
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                    ScheduleNames.Validate(requests, existingNames);
                    foreach (var item in requests) item.ScheduleName = (item.ScheduleName ?? "").Trim();
                    dialog.DialogResult = true;
                }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Schedule names", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            actions.Children.Add(cancel); actions.Children.Add(create);
            return dialog;
        }

        internal static Window BuildLayoutWindow(Window owner, BomLayout original, IList<ParameterField> fields, Action<BomLayout> accepted)
        {
            var dialog = Base(owner, "Modify Title / Fields", 1020, 680);
            var working = original.Clone();
            var columns = new ObservableCollection<BomColumn>(working.Columns);
            var layout = Rows(dialog, "Modify title and scheduled fields", "Category, Family, Type, Unit, Quantity and Element Count are required. Rename or reorder them; required fields cannot be removed or hidden.");
            var body = new System.Windows.Controls.Grid { Margin = new Thickness(0, 10, 0, 0) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            titleRow.Children.Add(new TextBlock { Text = "BOM title", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var title = new TextBox { Text = working.Title }; titleRow.Children.Add(title);
            body.Children.Add(titleRow);
            var fieldsGrid = new System.Windows.Controls.Grid();
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            System.Windows.Controls.Grid.SetRow(fieldsGrid, 1); body.Children.Add(fieldsGrid);
            var left = new DockPanel();
            var available = new ListBox { Margin = new Thickness(0, 8, 0, 8), DisplayMemberPath = "Name", BorderBrush = Brush("#DDD6CE"), BorderThickness = new Thickness(1) };
            left.Children.Add(new TextBlock { Text = "AVAILABLE COMMON PARAMETERS", FontSize = 11, FontWeight = FontWeights.SemiBold });
            DockPanel.SetDock(left.Children[0], Dock.Top);
            var add = Button(dialog, "Add field  →", false); DockPanel.SetDock(add, Dock.Bottom); left.Children.Add(add); left.Children.Add(available);
            fieldsGrid.Children.Add(left);
            var right = new DockPanel(); System.Windows.Controls.Grid.SetColumn(right, 2); fieldsGrid.Children.Add(right);
            var moves = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(moves, Dock.Bottom); right.Children.Add(moves);
            var selected = Grid(false); selected.ItemsSource = columns;
            var checkboxStyle = new Style(typeof(CheckBox)); checkboxStyle.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding("CanHide")));
            selected.Columns.Add(new DataGridCheckBoxColumn { Header = "Show", Binding = new Binding("IsVisible") { Mode = BindingMode.TwoWay }, ElementStyle = checkboxStyle, EditingElementStyle = checkboxStyle, Width = 60 });
            selected.Columns.Add(new DataGridTextColumn { Header = "Column heading", Binding = new Binding("Header") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 360 });
            selected.Columns.Add(new DataGridTextColumn { Header = "Rule", Binding = new Binding("Requirement"), IsReadOnly = true, Width = 85 });
            right.Children.Add(selected);
            Action refreshAvailable = () => available.ItemsSource = fields.Where(f => columns.All(c => c.Key != f.Key)).OrderBy(f => f.Name).ToList();
            refreshAvailable();
            add.Click += (s, e) =>
            {
                var field = available.SelectedItem as ParameterField; if (field == null) return;
                string label = field.Name; int suffix = 2;
                while (columns.Any(c => string.Equals(c.Header, label, StringComparison.OrdinalIgnoreCase))) label = field.Name + " (" + suffix++ + ")";
                var column = new BomColumn { Key = field.Key, Header = label }; columns.Add(column); selected.SelectedItem = column; refreshAvailable();
            };
            foreach (string text in new[] { "Move up", "Move down", "Remove" })
            {
                string action = text; var button = Button(dialog, text, false); moves.Children.Add(button);
                button.Click += (s, e) =>
                {
                    selected.CommitEdit(DataGridEditingUnit.Cell, true); selected.CommitEdit(DataGridEditingUnit.Row, true);
                    var column = selected.SelectedItem as BomColumn; if (column == null) return;
                    int index = columns.IndexOf(column);
                    if (action == "Remove")
                    {
                        if (column.IsRequired) { MessageBox.Show(dialog, "This is a required common field and cannot be removed.", "Required field"); return; }
                        columns.Remove(column); refreshAvailable();
                    }
                    else
                    {
                        int destination = index + (action == "Move up" ? -1 : 1);
                        if (destination >= 0 && destination < columns.Count) columns.Move(index, destination);
                    }
                };
            }
            layout.Children.Add(body); System.Windows.Controls.Grid.SetRow(body, 2);
            var actions = Actions(layout);
            var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close();
            var apply = Button(dialog, "Apply", true);
            apply.Click += (s, e) =>
            {
                try
                {
                    selected.CommitEdit(DataGridEditingUnit.Cell, true); selected.CommitEdit(DataGridEditingUnit.Row, true);
                    working.Title = title.Text.Trim(); working.Columns = columns.ToList(); working.Validate();
                    accepted(working); dialog.DialogResult = true;
                }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Modify fields", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            actions.Children.Add(cancel); actions.Children.Add(apply);
            return dialog;
        }

        private static Window Base(Window owner, string title, double width, double height)
        {
            return new Window { Title = title, Owner = owner, Width = width, Height = height, MinWidth = width - 100, MinHeight = height - 100,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background, FontFamily = owner.FontFamily,
                FontSize = 13, Resources = owner.Resources };
        }
        private static System.Windows.Controls.Grid Rows(Window window, string heading, string subtitle)
        {
            var layout = new System.Windows.Controls.Grid { Margin = new Thickness(24) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            layout.Children.Add(new TextBlock { Text = heading, FontSize = 25, FontWeight = FontWeights.SemiBold });
            var hint = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#716B63"), Margin = new Thickness(0, 10, 0, 18) };
            System.Windows.Controls.Grid.SetRow(hint, 1); layout.Children.Add(hint); window.Content = layout; return layout;
        }
        private static DataGrid Grid(bool readOnly)
        { return new DataGrid { AutoGenerateColumns = false, IsReadOnly = readOnly, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None, Background = Brushes.White, BorderBrush = Brush("#DDD6CE"), BorderThickness = new Thickness(1), RowHeight = 42 }; }
        private static StackPanel Actions(System.Windows.Controls.Grid layout)
        { var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom }; System.Windows.Controls.Grid.SetRow(panel, 3); layout.Children.Add(panel); return panel; }
        private static Button Button(Window window, string text, bool primary)
        { return new Button { Content = text, Style = window.FindResource(primary ? "PrimaryButton" : "SecondaryButton") as Style, Margin = new Thickness(6, 0, 0, 0) }; }
        private static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
    }
}
