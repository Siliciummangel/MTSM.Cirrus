using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MTSM.Cirrus.API.Security;

public sealed class TenantBoundaryFilter(ICirrusIdentityAccessor identityAccessor)
    : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values.TryGetValue("tenantId", out object? routeValue)
            && long.TryParse(routeValue?.ToString(), out long routeTenantId)
            && identityAccessor.GetRequiredIdentity().TenantId != routeTenantId)
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
