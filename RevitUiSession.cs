using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEP_Export_BOM_From_View_ver1
{
    internal sealed class RevitUiSession : IExternalEventHandler, IDisposable
    {
        private readonly Document document;
        private readonly Queue<Action> requests = new Queue<Action>();
        private readonly ExternalEvent externalEvent;
        private bool executing;
        private bool disposed;
        internal UIApplication Application { get; private set; }
        internal RevitUiSession(UIApplication application, Document doc)
        { Application = application; document = doc; externalEvent = ExternalEvent.Create(this); }
        internal void Run(Action action)
        {
            if (disposed) return;
            if (executing) { action(); return; }
            requests.Enqueue(action);
            var status = externalEvent.Raise();
            if (status != ExternalEventRequest.Accepted && status != ExternalEventRequest.Pending)
            { requests.Clear(); TaskDialog.Show("BOM Studio", "Revit is busy. Finish the current command, then retry this action."); }
        }
        public void Execute(UIApplication application)
        {
            if (disposed) return;
            Application = application;
            if (!document.IsValidObject)
            { requests.Clear(); TaskDialog.Show("BOM Studio", "The project used by this tool was closed. Close this tool and reopen it in the project you want to use."); return; }
            // Revit can return different managed wrappers for the same open document.
            // Document.Equals compares the underlying document in the Revit session.
            if (application.ActiveUIDocument == null || !document.Equals(application.ActiveUIDocument.Document))
            { requests.Clear(); TaskDialog.Show("BOM Studio", "Activate the project used by this tool before continuing: " + document.Title); return; }
            executing = true;
            try { while (requests.Count > 0 && !disposed) { var action = requests.Dequeue(); try { action(); } catch (Exception ex) { TaskDialog.Show("BOM Studio", ex.Message); } } }
            finally { executing = false; if (disposed) externalEvent.Dispose(); }
        }
        internal void Highlight(IList<ElementRecord> records)
        {
            var ui = Application.ActiveUIDocument;
            var references = new List<Reference>(); var zoomIds = new HashSet<ElementId>(); var points = new List<XYZ>();
            foreach (var record in records)
            {
                var link = record.LinkInstanceId == 0 ? null : document.GetElement(new ElementId(record.LinkInstanceId)) as RevitLinkInstance;
                var source = record.LinkInstanceId == 0 ? document : link?.GetLinkDocument();
                var element = source?.GetElement(new ElementId(record.ElementId));
                if (element == null || (record.UniqueId != null && element.UniqueId != record.UniqueId)) continue;
                references.Add(link == null ? new Reference(element) : new Reference(element).CreateLinkReference(link));
                zoomIds.Add(link == null ? element.Id : link.Id);
                var box = element.get_BoundingBox(null); if (box == null) continue;
                var transform = link == null ? box.Transform : link.GetTotalTransform().Multiply(box.Transform);
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X, y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            }
            if (references.Count == 0) throw new InvalidOperationException("Selected elements are no longer available. Refresh the model.");
            ui.Selection.SetReferences(references);
            try {
                ui.ShowElements(zoomIds.ToList());
                var view = ui.GetOpenUIViews().FirstOrDefault(v => v.ViewId == ui.ActiveView.Id);
                if (view != null && points.Count > 0) view.ZoomAndCenterRectangle(new XYZ(points.Min(p => p.X) - 1, points.Min(p => p.Y) - 1, points.Min(p => p.Z) - 1), new XYZ(points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1, points.Max(p => p.Z) + 1));
            } catch (Exception ex) { TaskDialog.Show("BOM selection", "Elements selected. Zoom was unavailable: " + ex.Message); }
        }
        public string GetName() { return "MEP BOM Studio actions"; }
        public void Dispose() { if (disposed) return; disposed = true; requests.Clear(); if (!executing) externalEvent.Dispose(); }
    }
}
