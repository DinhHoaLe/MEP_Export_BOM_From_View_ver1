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
    internal static partial class ConfigurationDialogs
    {
        internal static Window BuildQueryWindow(Window owner, PreviewSettings original, IList<ParameterField> fields, bool native, Action<PreviewSettings> accepted, Func<string, IList<ParameterField>> values = null)
        {
            var working = original.Clone();
            var dialog = Base(owner, native ? "Revit Schedule · Filter / Sorting & Grouping" : "BOM · Filter / Sorting & Grouping", 1080, 740);
            var layout = Rows(dialog, "Filter / Sorting & Grouping", native ?
                "All filters are combined with AND. Native Revit validates field/operator compatibility. Numeric values use project units; ElementId/global parameter filters require an ID." :
                "Filters apply to BOM rows, combined with AND. Quantity uses meters/pieces. Global parameter association uses a parameter field and ID, before aggregation. Totals remain separate by unit.");
            var tabs = new TabControl(); layout.Children.Add(tabs); System.Windows.Controls.Grid.SetRow(tabs, 2);
            var filterRows = new ObservableCollection<PreviewFilter>(working.Filters);
            var filters = Grid(false); filters.ItemsSource = filterRows;
            filters.Columns.Add(FieldColumn(fields));
            filters.Columns.Add(new DataGridComboBoxColumn { Header = "Condition", ItemsSource = PreviewSettings.Operators, SelectedItemBinding = new Binding("Operator") { Mode = BindingMode.TwoWay }, Width = 230 });
            filters.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 430 });
            var filterPanel = RulePanel(dialog, filters, () => { if (filterRows.Count < 8) filterRows.Add(new PreviewFilter { Field = fields.FirstOrDefault()?.Key }); }, () => { var row = filters.SelectedItem as PreviewFilter; if (row != null) filterRows.Remove(row); });
            if (values != null) {
                var valueActions = filterPanel.Children.OfType<StackPanel>().First();
                var choices = new ComboBox { Width = 280, DisplayMemberPath = "Name", SelectedValuePath = "Key", Margin = new Thickness(10, 0, 0, 0), ToolTip = "Choose a value for the selected filter rule" };
                var choose = Button(dialog, "Load values", false); valueActions.Children.Add(choose); valueActions.Children.Add(choices);
                filters.SelectionChanged += (s, e) => { if (e.Source == filters) choices.ItemsSource = null; };
                choose.Click += (s, e) => { try { filters.CommitEdit(DataGridEditingUnit.Cell, true); filters.CommitEdit(DataGridEditingUnit.Row, true); var row = filters.SelectedItem as PreviewFilter; if (row != null) choices.ItemsSource = values(row.Field); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Filter values"); } };
                choices.SelectionChanged += (s, e) => { var row = filters.SelectedItem as PreviewFilter; var choice = choices.SelectedItem as ParameterField; if (row != null && choice != null) { row.Value = choice.Key; filters.Items.Refresh(); } };
            }
            tabs.Items.Add(new TabItem { Header = "Filters", Content = filterPanel });
            var sortRows = new ObservableCollection<PreviewSort>(working.Sorts);
            var sorts = Grid(false); sorts.ItemsSource = sortRows; sorts.Columns.Add(FieldColumn(fields));
            foreach (var pair in new[] { new[] { "Descending", "Descending" }, new[] { "Header", "Header" }, new[] { "Footer", "Footer" }, new[] { "Footer title", "FooterTitle" }, new[] { "Footer count", "FooterCount" }, new[] { "Blank line", "BlankLine" } })
                sorts.Columns.Add(new DataGridCheckBoxColumn { Header = pair[0], Binding = new Binding(pair[1]) { Mode = BindingMode.TwoWay }, Width = 95 });
            var sorting = RulePanel(dialog, sorts, () => { if (sortRows.Count < 4) sortRows.Add(new PreviewSort { Field = fields.FirstOrDefault(f => sortRows.All(r => r.Field != f.Key))?.Key }); }, () => { var row = sorts.SelectedItem as PreviewSort; if (row != null) sortRows.Remove(row); });
            var moveActions = sorting.Children.OfType<StackPanel>().First();
            foreach (int direction in new[] { -1, 1 }) { int step = direction; var move = Button(dialog, step < 0 ? "Move up" : "Move down", false); moveActions.Children.Add(move); move.Click += (s, e) => { var row = sorts.SelectedItem as PreviewSort; if (row == null) return; int index = sortRows.IndexOf(row); if (index + step >= 0 && index + step < sortRows.Count) sortRows.Move(index, index + step); }; }
            var options = new WrapPanel { Margin = new Thickness(8) };
            var itemized = new CheckBox { Content = native ? "Itemize every instance" : "Itemize every instance (otherwise BOM groups)", IsChecked = working.Itemized, Margin = new Thickness(8) };
            var total = new CheckBox { Content = "Grand totals", IsChecked = working.GrandTotal, Margin = new Thickness(8) };
            var totalTitle = new CheckBox { Content = "Show total title", IsChecked = working.GrandTotalTitle, Margin = new Thickness(8) };
            var totalCount = new CheckBox { Content = "Show total count", IsChecked = working.GrandTotalCount, Margin = new Thickness(8) };
            var title = new TextBox { Text = working.TotalTitle, MinWidth = 160, Margin = new Thickness(8), ToolTip = "Grand total title" };
            foreach (var control in new UIElement[] { itemized, total, totalTitle, totalCount, title }) options.Children.Add(control);
            DockPanel.SetDock(options, Dock.Bottom); sorting.Children.Insert(0, options);
            tabs.Items.Add(new TabItem { Header = "Sorting / Grouping", Content = sorting });
            var actions = Actions(layout);
            var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close(); actions.Children.Add(cancel);
            var reset = Button(dialog, "Clear rules", false); reset.Click += (s, e) => { filterRows.Clear(); sortRows.Clear(); }; actions.Children.Add(reset);
            var apply = Button(dialog, "Apply", true); actions.Children.Add(apply);
            apply.Click += (s, e) => {
                try {
                    filters.CommitEdit(DataGridEditingUnit.Cell, true); filters.CommitEdit(DataGridEditingUnit.Row, true);
                    sorts.CommitEdit(DataGridEditingUnit.Cell, true); sorts.CommitEdit(DataGridEditingUnit.Row, true);
                    working.Filters = filterRows.ToList(); working.Sorts = sortRows.ToList(); working.Itemized = itemized.IsChecked == true;
                    working.GrandTotal = total.IsChecked == true; working.GrandTotalTitle = totalTitle.IsChecked == true; working.GrandTotalCount = totalCount.IsChecked == true; working.TotalTitle = title.Text;
                    working.Validate(fields.Select(f => f.Key), native); accepted(working); dialog.DialogResult = true;
                } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Filter / Sorting & Grouping"); }
            };
            return dialog;
        }
        private static DataGridComboBoxColumn FieldColumn(IList<ParameterField> fields)
        { return new DataGridComboBoxColumn { Header = "Field", ItemsSource = fields, DisplayMemberPath = "Name", SelectedValuePath = "Key", SelectedValueBinding = new Binding("Field") { Mode = BindingMode.TwoWay }, Width = 270 }; }
        private static DockPanel RulePanel(Window dialog, DataGrid grid, Action add, Action remove)
        {
            var panel = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
            var plus = Button(dialog, "Add rule", false); plus.Click += (s, e) => add();
            var minus = Button(dialog, "Remove rule", false); minus.Click += (s, e) => remove();
            actions.Children.Add(plus); actions.Children.Add(minus); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(grid); return panel;
        }

        internal static Window BuildNativeLayoutWindow(Window owner, NativeScheduleOptions original, Action<NativeScheduleOptions> accepted)
        {
            var working = original.Clone(); var columns = new ObservableCollection<NativeField>(working.Fields);
            var dialog = Base(owner, "Revit Schedule · Modify Title / Fields", 1040, 700);
            var layout = Rows(dialog, "Revit schedule title and fields", "Settings apply to this category only: preview, Excel export and created Revit schedule. Hidden fields can still be used for filtering and grouping.");
            var body = new DockPanel(); layout.Children.Add(body); System.Windows.Controls.Grid.SetRow(body, 2);
            var title = new TextBox { Text = working.Title, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(title, Dock.Top); body.Children.Add(title);
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
            var available = new ComboBox { DisplayMemberPath = "Name", Width = 250, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(available);
            Action refresh = () => available.ItemsSource = working.Available.Where(f => columns.All(c => c.Key != f.Key)).OrderBy(f => f.Name).ToList(); refresh();
            var grid = Grid(false); grid.ItemsSource = columns;
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Show", Binding = new Binding("Visible") { Mode = BindingMode.TwoWay }, Width = 70 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Name"), IsReadOnly = true, Width = 250 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Heading", Binding = new Binding("Heading") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 330 });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Calculate totals", Binding = new Binding("Total") { Mode = BindingMode.TwoWay }, Width = 140 }); body.Children.Add(grid);
            foreach (var label in new[] { "Add field", "Move up", "Move down", "Remove" }) {
                var action = label; var button = Button(dialog, label, false); actions.Children.Add(button);
                button.Click += (s, e) => {
                    grid.CommitEdit(); var column = grid.SelectedItem as NativeField;
                    if (action == "Add field") { var field = available.SelectedItem as NativeField; if (field != null) columns.Add(field.Clone()); }
                    else if (column != null) { int index = columns.IndexOf(column); if (action == "Remove") columns.Remove(column); else { int destination = index + (action == "Move up" ? -1 : 1); if (destination >= 0 && destination < columns.Count) columns.Move(index, destination); } }
                    refresh();
                };
            }
            var footer = Actions(layout); var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close(); footer.Children.Add(cancel);
            var apply = Button(dialog, "Apply", true); footer.Children.Add(apply);
            apply.Click += (s, e) => { try {
                grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (string.IsNullOrWhiteSpace(title.Text) || columns.Any(c => string.IsNullOrWhiteSpace(c.Heading)) || !columns.Any(c => c.Visible)) throw new InvalidOperationException("Enter a title and headings, and keep at least one visible field.");
                working.Title = title.Text.Trim(); working.Fields = columns.ToList();
                working.Settings.Validate(working.Fields.Select(f => f.Key), true); accepted(working); dialog.DialogResult = true;
            } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Schedule fields"); } };
            return dialog;
        }

        internal static Window BuildScheduleSelection(Window owner, IList<ScheduleRequest> requests)
        {
            var dialog = Base(owner, "Select Revit schedules for Excel", 820, 580);
            var layout = Rows(dialog, "Select schedules to export", "Each selected category becomes one worksheet in the same .xlsx file, using its own title, fields, filters and sorting/grouping.");
            var grid = Grid(false); grid.ItemsSource = requests;
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Select", Binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay }, Width = 65 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("CategoryName"), IsReadOnly = true, Width = 240 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Schedule title", Binding = new Binding("ScheduleName"), IsReadOnly = true, Width = 390 });
            layout.Children.Add(grid); System.Windows.Controls.Grid.SetRow(grid, 2);
            var actions = Actions(layout);
            foreach (bool select in new[] { true, false }) { bool value = select; var button = Button(dialog, select ? "All" : "None", false); button.Click += (s, e) => { foreach (var item in requests) item.IsSelected = value; grid.Items.Refresh(); }; actions.Children.Add(button); }
            var cancel = Button(dialog, "Cancel", false); cancel.Click += (s, e) => dialog.Close(); actions.Children.Add(cancel);
            var export = Button(dialog, "Export selected", true); actions.Children.Add(export);
            export.Click += (s, e) => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); if (requests.Any(r => r.IsSelected)) dialog.DialogResult = true; else MessageBox.Show(dialog, "Select at least one schedule."); };
            return dialog;
        }
        internal static Window BuildNamesWindow(Window owner, IList<ScheduleRequest> requests, IEnumerable<string> existingNames)
        {
            var dialog = Base(owner, "Create Revit Schedules", 900, 560);
            var layout = Rows(dialog, "Name each category schedule", requests.Any(r => r.Options?.ScopeElementIds != null) ?
                "Creates scoped host schedules and stores a read-only MEP BOM Scope Membership parameter on their elements (hidden column in schedules). Linked documents are not modified. The entire batch is one Revit Undo operation." :
                "Creates one schedule per category using configured fields, filters and sorting/grouping. Scope: entire host model.");
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
            var layout = Rows(dialog, "Modify title and scheduled fields", "Required fields cannot be removed. Category, Family, Type and Element Count stay visible; Unit and Quantity may be hidden. Enable other columns here when needed.");
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
