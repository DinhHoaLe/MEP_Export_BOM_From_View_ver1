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
