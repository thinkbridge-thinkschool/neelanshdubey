using Microsoft.AspNetCore.Authorization;
using QuotesApi.Extensions;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public class SameOwnerAuthorizationHandler : AuthorizationHandler<SameOwnerRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameOwnerRequirement requirement,
        Quote resource)
    {
        var userId = context.User.GetUserId();

        if (userId is not null && userId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
