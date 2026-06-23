using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HangerLayout.Revit;
using HangerLayout.UI;
using System.Collections.Generic;
using System.Linq;

namespace HangerLayout
{
    [Transaction(TransactionMode.Manual)]
    public class HangerLayoutCommand : IExternalCommand
    {
        private static HangerLayoutDialog? _instance;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_instance != null)
            {
                _instance.Activate();
                return Result.Succeeded;
            }

            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uiDoc.Document;

            // Load existing specs
            var specs = HangerSpecStore.Load(doc);

            // Pre-collect distinct service names from project for the All-Service dropdown
            var services = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var fp in new FilteredElementCollector(doc)
                .OfClass(typeof(FabricationPart))
                .WhereElementIsNotElementType()
                .Cast<FabricationPart>())
            {
                if (!string.IsNullOrWhiteSpace(fp.ServiceName))
                    services.Add(fp.ServiceName);
            }
            var sortedServices = services.OrderBy(s => s, System.StringComparer.OrdinalIgnoreCase).ToList();

            var dialog = new HangerLayoutDialog(uiDoc, specs, sortedServices);
            _instance = dialog;
            dialog.Closed += (_, _) => _instance = null;
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
