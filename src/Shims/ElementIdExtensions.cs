using Autodesk.Revit.DB;

namespace HangerLayout.Revit
{
    internal static class ElementIdExtensions
    {
        /// <summary>
        /// The ElementId's numeric value as a <see cref="long"/>, across Revit versions. Revit 2024+ expose
        /// <c>ElementId.Value</c> (long); Revit 2023 only has <c>IntegerValue</c> (int). Call this instead of
        /// <c>.Value</c> so the code compiles on every target. The <c>REVIT2023</c> symbol is defined by the
        /// csproj (<c>DefineConstants</c> = <c>REVIT$(RevitVersion)</c>).
        /// </summary>
        public static long ToIdValue(this ElementId id)
        {
#if REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        /// <summary>
        /// Construct an <see cref="ElementId"/> from a <see cref="long"/> across Revit versions. Revit 2024+ have
        /// an <c>ElementId(long)</c> constructor; Revit 2023 only has <c>ElementId(int)</c>. Use this to round-trip
        /// a value produced by <see cref="ToIdValue"/>.
        /// </summary>
        public static ElementId ToElementId(this long value)
        {
#if REVIT2023
            return new ElementId((int)value);
#else
            return new ElementId(value);
#endif
        }
    }
}
