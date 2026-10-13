using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEP_Export_BOM_From_View_ver1
{
    [Transaction(TransactionMode.Manual)]
    public class ExportBomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (commandData.Application.ActiveUIDocument == null)
            {
                TaskDialog.Show("Export BOM", "Please open a Revit project first.");
                return Result.Cancelled;
            }
            try
            {
                var window = new ExportBomWindow(commandData.Application.ActiveUIDocument.Document);
                window.ShowDialog(commandData.Application.MainWindowHandle);
                if (window.HighlightRequests.Count > 0)
                {
                    var ui = commandData.Application.ActiveUIDocument;
                    var references = new System.Collections.Generic.List<Reference>();
                    var zoomIds = new System.Collections.Generic.List<ElementId>();
                    var zoomPoints = new System.Collections.Generic.List<XYZ>();
                    foreach (var record in window.HighlightRequests)
                    {
                        var sourceLink = record.LinkInstanceId == 0 ? null : ui.Document.GetElement(new ElementId(record.LinkInstanceId)) as RevitLinkInstance;
                        var sourceDocument = sourceLink == null ? ui.Document : sourceLink.GetLinkDocument();
                        var box = sourceDocument?.GetElement(new ElementId(record.ElementId))?.get_BoundingBox(null);
                        if (box != null) { var transform = sourceLink == null ? box.Transform : sourceLink.GetTotalTransform().Multiply(box.Transform); for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) zoomPoints.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X, y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z))); }
                        if (record.LinkInstanceId == 0) { var element = ui.Document.GetElement(new ElementId(record.ElementId)); if (element != null) { references.Add(new Reference(element)); zoomIds.Add(element.Id); } }
                        else { var link = ui.Document.GetElement(new ElementId(record.LinkInstanceId)) as RevitLinkInstance; var linkedElement = link?.GetLinkDocument()?.GetElement(new ElementId(record.ElementId)); if (linkedElement != null) { references.Add(new Reference(linkedElement).CreateLinkReference(link)); zoomIds.Add(link.Id); } }
                    }
                    ui.Selection.SetReferences(references);
                    try {
                        if (zoomIds.Count > 0) ui.ShowElements(zoomIds);
                        if (zoomPoints.Count > 0) {
                            var view = System.Linq.Enumerable.FirstOrDefault(ui.GetOpenUIViews(), v => v.ViewId == ui.ActiveView.Id);
                            if (view != null) view.ZoomAndCenterRectangle(new XYZ(System.Linq.Enumerable.Min(zoomPoints, p => p.X) - 1, System.Linq.Enumerable.Min(zoomPoints, p => p.Y) - 1, System.Linq.Enumerable.Min(zoomPoints, p => p.Z) - 1),
                                new XYZ(System.Linq.Enumerable.Max(zoomPoints, p => p.X) + 1, System.Linq.Enumerable.Max(zoomPoints, p => p.Y) + 1, System.Linq.Enumerable.Max(zoomPoints, p => p.Z) + 1));
                        }
                    } catch (Exception ex) { TaskDialog.Show("BOM selection", "Elements selected. Zoom was unavailable: " + ex.Message); }
                }
                if (window.ScheduleRequests.Count > 0)
                {
                    var schedules = RevitScheduleService.CreateBatch(commandData.Application.ActiveUIDocument.Document, window.ScheduleRequests);
                    var schedule = schedules[0];
                    try { commandData.Application.ActiveUIDocument.ActiveView = schedule; }
                    catch (Exception ex)
                    { TaskDialog.Show("BOM schedule created", "Created " + schedule.Name + ". Open it in Project Browser.\n" + ex.Message); }
                    if (schedules.Count > 1) TaskDialog.Show("BOM schedules created", "Created " + schedules.Count + " schedules. The first schedule is open; the others are in Project Browser.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

    }
}
