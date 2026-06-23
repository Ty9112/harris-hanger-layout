using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Allows MEP Fabrication pipework + ductwork elements only.
    /// Used by the Hanger Layout dialog's Pick Elements flow.
    /// </summary>
    internal class FabricationPipeOrDuctFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is not FabricationPart fp) return false;
            if (fp.Category == null) return false;
            long catVal = fp.Category.Id.Value;
            return catVal == (long)BuiltInCategory.OST_FabricationPipework
                || catVal == (long)BuiltInCategory.OST_FabricationDuctwork;
        }

        // For PickObject(ObjectType.PointOnElement, ...) Revit calls this with
        // the reference about to be returned. Returning false here vetoes the
        // click regardless of AllowElement — must defer to the element check.
        public bool AllowReference(Reference reference, XYZ position)
        {
            // No Document context here; reference.ElementId is enough — Pick
            // already validated AllowElement against the parent.
            return reference != null && reference.ElementId != ElementId.InvalidElementId;
        }
    }
}
