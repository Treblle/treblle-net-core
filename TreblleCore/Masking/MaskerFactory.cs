using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using Treblle.Runtime.Masking;

namespace Treblle.Net.Core.Masking;

/// <summary>
/// Factory for creating string masker instances. Replaces keyed services for .NET 6+ compatibility.
/// </summary>
internal class MaskerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _maskerTypes;

    public MaskerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        
        // Map masker names to their types
        _maskerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { nameof(DefaultStringMasker), typeof(DefaultStringMasker) },
            { nameof(EmailMasker), typeof(EmailMasker) },
            { nameof(CreditCardMasker), typeof(CreditCardMasker) },
            { nameof(SocialSecurityMasker), typeof(SocialSecurityMasker) },
            { nameof(DateMasker), typeof(DateMasker) },
            { nameof(PostalCodeMasker), typeof(PostalCodeMasker) },
            { nameof(AuthorizationMasker), typeof(AuthorizationMasker) }
        };
    }

    /// <summary>
    /// Gets a masker instance by name, with fallback to DefaultStringMasker
    /// </summary>
    public IStringMasker GetMasker(string maskerName)
    {
        if (string.IsNullOrWhiteSpace(maskerName))
            return _serviceProvider.GetRequiredService<DefaultStringMasker>();

        if (_maskerTypes.TryGetValue(maskerName, out var maskerType))
        {
            return (IStringMasker)_serviceProvider.GetRequiredService(maskerType);
        }

        // Fallback to default masker if unknown type
        return _serviceProvider.GetRequiredService<DefaultStringMasker>();
    }
}