using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Auth;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.UnitTests.Auth;

public class SessionPolicyTests
{
    private static DateTimeOffset LocalAt(int hour, int minute = 0) =>
        new(2026, 9, 10, hour, minute, 0, TimeSpan.FromHours(5));   // PKT

    [Fact]
    public void A_clerk_signing_in_during_the_day_is_logged_out_at_eight()
    {
        // SE-06. The tablet is left on the packing bench overnight.
        var expiry = SessionPolicy.AbsoluteExpiryFor([Roles.Clerk], LocalAt(9, 30));

        expiry.Should().NotBeNull();
        expiry!.Value.Hour.Should().Be(20);
        expiry.Value.Day.Should().Be(10);
    }

    [Fact]
    public void A_clerk_signing_in_after_eight_gets_until_eight_tomorrow()
    {
        // An evening firing still has to be recorded. A session that has already expired
        // at the moment it is issued would mean the clerk simply cannot log in.
        var expiry = SessionPolicy.AbsoluteExpiryFor([Roles.Clerk], LocalAt(21, 15));

        expiry.Should().NotBeNull();
        expiry!.Value.Hour.Should().Be(20);
        expiry.Value.Day.Should().Be(11);
    }

    [Fact]
    public void An_owner_session_slides_and_has_no_hard_stop()
    {
        // SE-07. The owner comes back to the screen after an hour on the floor.
        SessionPolicy.AbsoluteExpiryFor([Roles.Owner], LocalAt(9, 30)).Should().BeNull();
    }

    [Fact]
    public void An_administrator_session_slides_and_has_no_hard_stop()
    {
        SessionPolicy.AbsoluteExpiryFor([Roles.Administrator], LocalAt(9, 30)).Should().BeNull();
    }

    [Fact]
    public void Someone_who_is_both_owner_and_clerk_is_not_logged_out_at_eight()
    {
        // The more permissive role wins. Otherwise giving the owner a clerk role to let
        // him cover the desk would quietly cut his own sessions short.
        SessionPolicy.AbsoluteExpiryFor([Roles.Clerk, Roles.Owner], LocalAt(9, 30))
            .Should().BeNull();
    }

    [Fact]
    public void The_effective_expiry_is_whichever_comes_first()
    {
        var now = new DateTimeOffset(2026, 9, 10, 19, 45, 0, TimeSpan.FromHours(5));
        var principal = TestPrincipal.With(SessionPolicy.AbsoluteExpiryClaim,
            new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.FromHours(5)).ToString("O"));

        // Sliding would give 20:15; the clerk's hard stop at 20:00 must win.
        SessionPolicy.EffectiveExpiry(principal, now)
            .Should().Be(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.FromHours(5)));
    }

    [Fact]
    public void With_no_hard_stop_the_effective_expiry_is_the_sliding_window()
    {
        var now = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(5));

        SessionPolicy.EffectiveExpiry(TestPrincipal.Empty(), now)
            .Should().Be(now.Add(SessionPolicy.SlidingWindow));
    }
}
