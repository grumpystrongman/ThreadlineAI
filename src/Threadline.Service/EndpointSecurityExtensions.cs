using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Threadline.Service;

public static class EndpointSecurityExtensions
{
    public static RouteGroupBuilder RequireThreadlineLocalAccess(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<ThreadlineServiceOptions>();
            if (options.IsAuthorized(context.HttpContext.Request))
            {
                return await next(context);
            }

            return Results.Json(new { error = "Threadline local API access denied." }, statusCode: StatusCodes.Status401Unauthorized);
        });

        return group;
    }
}
