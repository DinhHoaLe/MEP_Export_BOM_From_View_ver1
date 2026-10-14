using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MEP_Export_BOM_From_View_ver1
{
    internal sealed partial class ExportBomWindow
    {
        private void ViewElementIds(object sender, RoutedEventArgs e)
        {
            var button = e.OriginalSource as Button;
            var row = button?.Tag as BomRow;
            if (row == null || row.IsDecoration || result == null) return;
            var keys = new HashSet<string>(row.RecordKeys);
            var records = result.Elements.Where(record => keys.Contains(record.Identity)).ToList();
            if (records.Count == 0) return;
            e.Handled = true;
            ConfigurationDialogs.BuildElementIdsWindow(window, records,
                record => RunInRevit(() => ConfigurationDialogs.BuildPropertiesWindow(window, new[] { record }, item => BomService.GetProperties(document, item)).ShowDialog()),
                selected => ViewInRevit(selected)).Show();
        }

        private void ShowBomProperties(MouseButtonEventArgs e)
        {
            if (result == null) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            DataGridRow container = null;
            while (source != null)
            {
                container = source as DataGridRow; if (container != null) break;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            var row = container?.Item as BomRow;
            if (row == null || row.IsDecoration) return;
            var keys = new HashSet<string>(row.RecordKeys);
            var records = result.Elements.Where(record => keys.Contains(record.Identity)).ToList();
            if (records.Count == 0) return;
            e.Handled = true;
            RunInRevit(() => ConfigurationDialogs.BuildPropertiesWindow(window, records, record => BomService.GetProperties(document, record)).ShowDialog());
        }
    }
}
