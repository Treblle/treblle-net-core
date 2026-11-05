using System.Collections.Generic;

namespace Treblle.Net.Core;

/// <summary>
/// Attaches custom key/value metadata to the Treblle payload for the current request.
/// Resolve it from dependency injection anywhere inside the request — a controller,
/// a minimal API handler, an action filter or a plain service class.
/// </summary>
/// <example>
/// <code>
/// public sealed class OrdersController : ControllerBase
/// {
///     private readonly ITreblleMetadata _treblle;
///
///     public OrdersController(ITreblleMetadata treblle) => _treblle = treblle;
///
///     [HttpPost]
///     public IActionResult Store()
///     {
///         _treblle.Meta("tenant_id", tenantId);
///         return Ok();
///     }
/// }
/// </code>
/// </example>
public interface ITreblleMetadata
{
    /// <summary>
    /// Adds a single metadata entry. Calling this again with the same key overwrites the
    /// previous value, so it is safe to call repeatedly during a request.
    /// </summary>
    /// <param name="key">Metadata key. Null, empty or whitespace keys are ignored.</param>
    /// <param name="value">
    /// Any value. Primitives, <see cref="System.Guid"/>, <see cref="System.DateTime"/> and
    /// enums are kept as-is; anything else is serialized to JSON when the payload is built.
    /// </param>
    void Meta(string key, object? value);

    /// <summary>
    /// Adds several metadata entries at once. Equivalent to calling
    /// <see cref="Meta(string, object?)"/> for each pair — existing keys are overwritten,
    /// keys not present are left alone.
    /// </summary>
    /// <param name="values">Entries to merge in. A null dictionary is ignored.</param>
    void Meta(IDictionary<string, object?> values);
}
