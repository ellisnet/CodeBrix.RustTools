namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// A requested target that has no native file in the wrapper.
/// </summary>
internal sealed class MissingTargetModel
{
    /// <summary>The RID.</summary>
    public string Rid { get; set; }

    /// <summary>Why it is missing.</summary>
    public string Reason { get; set; }
}
