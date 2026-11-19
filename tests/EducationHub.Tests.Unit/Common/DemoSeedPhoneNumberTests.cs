using EducationHub.API.Seed;
using EducationHub.Application.Common.Helpers;

namespace EducationHub.Tests.Unit.Common;

/// <summary>
/// QA-07: the demo parents' sign-in numbers collided with numbers another demo seeder had already
/// given to account-less parent profiles, so DemoStaffSeeder gave parent.one/two no number at all
/// and "01000000001" signed nobody in. The numbers the seeders hand out must stay disjoint.
/// </summary>
public sealed class DemoSeedPhoneNumberTests
{
    private static string Canonical(string phone) => PhoneNumberFormat.ToCanonical(phone)!;

    [Fact]
    public void Demo_parent_account_numbers_are_not_used_by_the_demo_parent_profiles()
    {
        var profiles = DemoOperationsSeeder.DemoParentProfilePhones.Select(Canonical).ToHashSet();

        Assert.NotEmpty(profiles);
        Assert.All(DemoStaffSeeder.DemoParentPhones, entry => Assert.DoesNotContain(Canonical(entry.Phone), profiles));
    }

    [Fact]
    public void Demo_parent_account_numbers_are_distinct_and_valid()
    {
        var numbers = DemoStaffSeeder.DemoParentPhones.Select(static p => Canonical(p.Phone)).ToList();

        Assert.All(numbers, Assert.NotNull);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public void The_demo_request_for_parent_one_uses_parent_ones_number_and_no_profile_number()
    {
        var parentOne = Canonical(DemoStaffSeeder.DemoParentPhones.Single(p => p.Email.StartsWith("parent.one@", StringComparison.Ordinal)).Phone);
        var request = DemoRegistrationRequestSeeder.DemoRequests.Single(r => r.Guardian == "Demo Parent 1");

        Assert.Equal(parentOne, Canonical(request.Phone));

        // The other demo requests are new families: their numbers belong to no demo account or profile.
        var taken = DemoOperationsSeeder.DemoParentProfilePhones
            .Concat(DemoStaffSeeder.DemoParentPhones.Select(static p => p.Phone))
            .Select(Canonical)
            .ToHashSet();

        Assert.All(
            DemoRegistrationRequestSeeder.DemoRequests.Where(r => r.Guardian != "Demo Parent 1"),
            r => Assert.DoesNotContain(Canonical(r.Phone), taken));
    }
}
