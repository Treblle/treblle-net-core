using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using Treblle.Runtime.Masking;

namespace Treblle.Net.Core.Masking;

/// <summary>
/// Resolves string maskers by name. Replaces keyed services for .NET 6+ compatibility.
/// </summary>
/// <remarks>
/// The maskers are resolved once here rather than on every lookup. The previous version hit
/// the container for each masked property — and the caller hit the container again for the
/// factory itself, once per property of every payload.
/// </remarks>
internal sealed class MaskerFactory
{
    private readonly Dictionary<string, IStringMasker> _maskers;
    private readonly IStringMasker _defaultMasker;

    public MaskerFactory(IServiceProvider serviceProvider)
    {
        _defaultMasker = serviceProvider.GetRequiredService<DefaultStringMasker>();

        _maskers = new Dictionary<string, IStringMasker>(StringComparer.OrdinalIgnoreCase)
        {
            { nameof(DefaultStringMasker), _defaultMasker },
            { nameof(EmailMasker), serviceProvider.GetRequiredService<EmailMasker>() },
            { nameof(CreditCardMasker), serviceProvider.GetRequiredService<CreditCardMasker>() },
            { nameof(SocialSecurityMasker), serviceProvider.GetRequiredService<SocialSecurityMasker>() },
            { nameof(DateMasker), serviceProvider.GetRequiredService<DateMasker>() },
            { nameof(PostalCodeMasker), serviceProvider.GetRequiredService<PostalCodeMasker>() },
            { nameof(AuthorizationMasker), serviceProvider.GetRequiredService<AuthorizationMasker>() }
        };
    }

    /// <summary>
    /// Gets a masker by name, falling back to <see cref="DefaultStringMasker"/> for an unknown
    /// or blank name.
    /// </summary>
    public IStringMasker GetMasker(string? maskerName)
    {
        if (!string.IsNullOrWhiteSpace(maskerName) && _maskers.TryGetValue(maskerName!, out var masker))
        {
            return masker;
        }

        return _defaultMasker;
    }
}
