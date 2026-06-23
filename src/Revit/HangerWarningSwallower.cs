using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Suppresses non-fatal connection / piping-network warnings that fire when
    /// FabricationPart.PlaceAsHanger anchors a hanger to a host that's already
    /// part of an MEP network. Without this, dozens of cascading modal dialogs
    /// can interrupt the apply pass.
    /// </summary>
    internal class HangerWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var fail in a.GetFailureMessages())
            {
                var sev = fail.GetSeverity();
                if (sev == FailureSeverity.Warning)
                    a.DeleteWarning(fail);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
