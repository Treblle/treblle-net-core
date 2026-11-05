namespace Treblle.Net.Core;

/// <summary>
/// Constants used throughout the Treblle SDK
/// </summary>
public static class TreblleConstants
{
    /// <summary>
    /// Payload version number. Update this when making significant changes to the payload structure.
    /// Starting from version 20 to differentiate from previous string-based versioning.
    /// </summary>
    public const int PayloadVersion = 20;
}

/// <summary>
/// Keys for values the SDK stashes on <c>HttpContext.Items</c>. Object identity rather than a
/// string literal, so nothing can collide with an application's own keys and the value cannot
/// be read back as the wrong type.
/// </summary>
internal static class TreblleItemKeys
{
    internal static readonly object ElapsedMilliseconds = new();
}
