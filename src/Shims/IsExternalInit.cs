#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Enables C# 9+ <c>init</c>-only property accessors when targeting .NET Framework (net48, Revit 2023/2024).
    /// The C# compiler requires this marker type to exist for <c>init</c> setters; .NET Framework doesn't ship it,
    /// so we provide it. Compiler-only — never referenced at runtime. (net8 ships its own, hence the guard.)
    /// </summary>
    internal static class IsExternalInit { }
}
#endif
