using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace MEP_Export_BOM_From_View_ver1
{
    [Transaction(TransactionMode.Manual)]
    public class ExportBomCommand : IExternalCommand
    {
        private static ExportBomWindow current;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var application = commandData.Application;
            if (application.ActiveUIDocument == null) { TaskDialog.Show("Export BOM", "Please open a Revit project first."); return Result.Cancelled; }
            try
            {
                if (current != null && current.IsOpen) { current.Activate(); return Result.Succeeded; }
                var document = application.ActiveUIDocument.Document;
                var session = new RevitUiSession(application, document);
                try { current = new ExportBomWindow(document, session); current.Show(application.MainWindowHandle); }
                catch { session.Dispose(); throw; }
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.ToString(); return Result.Failed; }
        }
    }
}
