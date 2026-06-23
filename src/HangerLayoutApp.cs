using System;
using System.Reflection;
using Autodesk.Revit.UI;
using HangerLayout.Revit;

namespace HangerLayout
{
    /// <summary>
    /// Revit IExternalApplication entry point.
    /// Creates a "Hanger Layout" ribbon tab with a single button that opens
    /// the modeless layout dialog, and registers the shared
    /// <see cref="RevitEventHandler"/> pair the dialog uses to call back into
    /// the Revit API thread.
    /// </summary>
    public class HangerLayoutApp : IExternalApplication
    {
        // Exposed so HangerLayoutDialog can post actions back to the Revit
        // API thread via ExternalEvent.Raise(). Nullable because they're
        // only valid between OnStartup and OnShutdown.
        public static RevitEventHandler? HangerHandler { get; private set; }
        public static ExternalEvent?     HangerEvent   { get; private set; }

        private const string RibbonTabName  = "Hanger Layout";
        private const string RibbonPanelName = "Layout";

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                HangerHandler = new RevitEventHandler();
                HangerEvent   = ExternalEvent.Create(HangerHandler);

                try { app.CreateRibbonTab(RibbonTabName); }
                catch (Exception) { /* tab already exists */ }

                var panel = app.CreateRibbonPanel(RibbonTabName, RibbonPanelName);

                string asm = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    name: "HangerLayout",
                    text: "Hanger\nLayout",
                    assemblyName: asm,
                    className: "HangerLayout.HangerLayoutCommand")
                {
                    ToolTip = "Place hangers along selected fabrication pipes and ducts.",
                    LongDescription =
                        "Opens a modeless dialog to define size-banded Support Specifications " +
                        "(Pipe and Duct) and apply them. Hangers are placed at " +
                        "Straight Spacing intervals with Fitting Distance / Distance from " +
                        "Joint setbacks at each end. Specs persist in the project.",
                    LargeImage = RibbonIconFactory.Hanger(32),
                    Image      = RibbonIconFactory.Hanger(16),
                };

                panel.AddItem(buttonData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Hanger Layout — startup error", ex.ToString());
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}
