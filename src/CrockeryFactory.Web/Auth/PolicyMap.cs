using CrockeryFactory.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CrockeryFactory.Web.Auth;

/// <summary>
/// The single definition of which roles satisfy which policy.
///
/// Policy registration and the permission list returned to the client both read this
/// map, so the UI cannot be told a user may do something the server will refuse - the
/// two answers come from the same table rather than from two lists kept in step by hand.
///
/// Adding the Accountant role in phase 2 is an edit to this map and nothing else
/// (spec section 4.4).
/// </summary>
public static class PolicyMap
{
    public static readonly IReadOnlyDictionary<string, string[]> RolesByPolicy =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Policies.CanRecordTransactions] = [Roles.Clerk, Roles.Owner],
            [Policies.CanAdjustStock] = [Roles.Clerk, Roles.Owner],
            [Policies.CanViewReports] = [Roles.Clerk, Roles.Owner, Roles.Administrator],
            [Policies.CanManageCatalogue] = [Roles.Owner],
            [Policies.CanSetPrices] = [Roles.Owner],
            [Policies.CanCancelHistorical] = [Roles.Owner],
            [Policies.CanManageUsers] = [Roles.Owner, Roles.Administrator],
            [Policies.CanManageSettings] = [Roles.Owner, Roles.Administrator],
            [Policies.CanViewAudit] = [Roles.Owner, Roles.Administrator]
        };

    public static void AddAll(AuthorizationBuilder builder)
    {
        foreach (var (policy, roles) in RolesByPolicy)
            builder.AddPolicy(policy, p => p.RequireRole(roles));
    }

    /// <summary>What this user may do, for the client to hide what it should not offer.</summary>
    public static IReadOnlyList<string> PermissionsFor(IEnumerable<string> roles)
    {
        var held = roles.ToHashSet(StringComparer.Ordinal);

        return RolesByPolicy
            .Where(entry => entry.Value.Any(held.Contains))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
