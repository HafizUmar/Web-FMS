using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Auth;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.UnitTests.Auth;

public class PolicyMapTests
{
    [Fact]
    public void Every_declared_policy_has_a_role_that_satisfies_it()
    {
        // A policy nobody can satisfy is an endpoint nobody can reach - a lockout that
        // only shows up when someone tries to use the feature.
        PolicyMap.RolesByPolicy.Values.Should().OnlyContain(roles => roles.Length > 0);
    }

    [Fact]
    public void Every_policy_constant_is_registered()
    {
        // Catches a policy added to the constants and forgotten in the map, which would
        // fail closed at runtime with an unhelpful message.
        var declared = typeof(Policies).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        declared.Should().NotBeEmpty();
        PolicyMap.RolesByPolicy.Keys.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void Only_the_owner_may_set_prices_or_cancel_history()
    {
        PolicyMap.RolesByPolicy[Policies.CanSetPrices].Should().Equal(Roles.Owner);
        PolicyMap.RolesByPolicy[Policies.CanCancelHistorical].Should().Equal(Roles.Owner);
        PolicyMap.RolesByPolicy[Policies.CanManageCatalogue].Should().Equal(Roles.Owner);
    }

    [Fact]
    public void An_administrator_cannot_record_transactions_or_adjust_stock()
    {
        // Spec section 4.2: the admin manages users and settings. Letting the account
        // that can create users also move stock removes the separation entirely.
        PolicyMap.RolesByPolicy[Policies.CanRecordTransactions].Should().NotContain(Roles.Administrator);
        PolicyMap.RolesByPolicy[Policies.CanAdjustStock].Should().NotContain(Roles.Administrator);
    }

    [Fact]
    public void A_clerk_gets_the_transaction_permissions_and_nothing_else()
    {
        var permissions = PolicyMap.PermissionsFor([Roles.Clerk]);

        permissions.Should().Contain(Policies.CanRecordTransactions);
        permissions.Should().Contain(Policies.CanAdjustStock);
        permissions.Should().Contain(Policies.CanViewReports);

        permissions.Should().NotContain(Policies.CanSetPrices);
        permissions.Should().NotContain(Policies.CanManageUsers);
        permissions.Should().NotContain(Policies.CanViewAudit);
    }

    [Fact]
    public void Someone_with_no_role_may_do_nothing()
    {
        PolicyMap.PermissionsFor([]).Should().BeEmpty();
    }
}
