using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

public class SameOwnerRequirement : IAuthorizationRequirement
{
}
