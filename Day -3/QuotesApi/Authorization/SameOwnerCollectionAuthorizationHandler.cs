using Microsoft.AspNetCore.Authorization;
using QuotesApi.Extensions;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

// A second handler for the same SameOwnerRequirement, scoped to Collection
// instead of Quote -- AuthorizationHandler<TRequirement, TResource> is
// resource-type-specific, so the Quote handler (which resolves against
// Quote.OwnerId) can't also resolve against Collection.OwnerId.
public class SameOwnerCollectionAuthorizationHandler : AuthorizationHandler<SameOwnerRequirement, Collection>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameOwnerRequirement requirement,
        Collection resource)
    {
        var userId = context.User.GetUserId();

        if (userId is not null && userId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
