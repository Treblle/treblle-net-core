using Microsoft.AspNetCore.Builder;

namespace Treblle.Net.Core;

public static class RouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder UseTreblle(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new TreblleAttribute());
        
        return builder;
    }
}
