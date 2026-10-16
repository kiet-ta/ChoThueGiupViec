using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>MAIN-FLOW-FIX: the bank account the agency's payout is transferred to.</summary>
public class AgencyBankTests
{
    private sealed class InMemoryBank : IAgencyBankRepository
    {
        public Dictionary<int, (string Name, string Account)> Rows { get; } = new() { [7] = ("", "") };

        public Task<(string BankName, string BankAccountNo)?> GetAsync(int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(string BankName, string BankAccountNo)?>(Rows.TryGetValue(agencyId, out var r) ? r : null);

        public Task<bool> SetAsync(int agencyId, string bankName, string bankAccountNo, CancellationToken cancellationToken = default)
        {
            if (!Rows.ContainsKey(agencyId))
            {
                return Task.FromResult(false);
            }

            Rows[agencyId] = (bankName, bankAccountNo);
            return Task.FromResult(true);
        }
    }

    private static (AgencyBankService Service, InMemoryBank Repo) Create(int? agencyId = 7, UserRole role = UserRole.Partner)
    {
        var user = new FakeCurrentUser();
        if (agencyId is int id)
        {
            user.SignIn(id, role);
        }

        var repo = new InMemoryBank();
        return (new AgencyBankService(repo, user), repo);
    }

    private static ValidationException Rejected(Action act) => Assert.Throws<ValidationException>(act);

    [Fact]
    public void Both_fields_may_be_left_out_at_registration()
    {
        Assert.Equal(("", ""), AgencyBankValidator.Validate(null, null, required: false));
        Assert.Equal(("", ""), AgencyBankValidator.Validate("  ", " ", required: false));
    }

    [Fact]
    public void A_valid_pair_is_trimmed_and_the_account_loses_its_spaces()
    {
        Assert.Equal(("Vietcombank", "0011001234567"), AgencyBankValidator.Validate("  Vietcombank ", "0011 0012 34567", required: true));
    }

    [Fact]
    public void Giving_only_one_of_the_two_is_an_error_even_at_registration()
    {
        Assert.True(Rejected(() => AgencyBankValidator.Validate("VCB", null, required: false)).Errors.ContainsKey("bankAccountNo"));
        Assert.True(Rejected(() => AgencyBankValidator.Validate(null, "0011001234", required: false)).Errors.ContainsKey("bankName"));
    }

    [Fact]
    public void The_update_requires_both_fields()
    {
        var errors = Rejected(() => AgencyBankValidator.Validate(null, null, required: true)).Errors;

        Assert.Contains("bankName", errors.Keys);
        Assert.Contains("bankAccountNo", errors.Keys);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345678901234567890123456789012")]
    [InlineData("00110012A4")]
    public void An_account_number_must_be_6_to_30_digits(string account) =>
        Assert.Contains("bankAccountNo", Rejected(() => AgencyBankValidator.Validate("VCB", account, required: true)).Errors.Keys);

    [Fact]
    public void A_bank_name_over_the_column_size_is_rejected() =>
        Assert.Contains("bankName", Rejected(() => AgencyBankValidator.Validate(new string('B', 101), "0011001234", required: true)).Errors.Keys);

    [Fact]
    public void Registration_validation_carries_the_bank_fields_and_reports_them_with_the_other_errors()
    {
        var ok = AgencyRegistrationValidator.Validate(
            new RegisterAgencyRequest("0312345678", "Cong ty", "Rep", "0901234567", "a@example.com", "Passw0rd!Passw0rd", "Vietcombank", "0011001234"), 10);
        Assert.Equal(("Vietcombank", "0011001234"), (ok.BankName, ok.BankAccountNo));

        var none = AgencyRegistrationValidator.Validate(
            new RegisterAgencyRequest("0312345678", "Cong ty", "Rep", "0901234567", "a@example.com", "Passw0rd!Passw0rd"), 10);
        Assert.Equal(("", ""), (none.BankName, none.BankAccountNo));

        var bad = Rejected(() => AgencyRegistrationValidator.Validate(
            new RegisterAgencyRequest("bad", "Cong ty", "Rep", "0901234567", "a@example.com", "Passw0rd!Passw0rd", "VCB", "12"), 10)).Errors;
        Assert.Contains("taxCode", bad.Keys);
        Assert.Contains("bankAccountNo", bad.Keys);
    }

    [Fact]
    public async Task The_partner_sets_and_reads_back_the_account()
    {
        var (service, repo) = Create();
        Assert.False((await service.GetAsync()).IsSet);

        var saved = await service.SetAsync(new UpdateAgencyBankRequest("Vietcombank", "0011001234567"));

        Assert.True(saved.IsSet);
        Assert.Equal(("Vietcombank", "0011001234567"), repo.Rows[7]);
        Assert.Equal("0011001234567", (await service.GetAsync()).BankAccountNo);
    }

    [Fact]
    public async Task Only_a_partner_can_use_it_and_a_missing_body_is_a_validation_error()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Create(null).Service.GetAsync());
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Create(7, UserRole.Admin).Service.SetAsync(new UpdateAgencyBankRequest("VCB", "0011001234")));
        await Assert.ThrowsAsync<ValidationException>(() => Create().Service.SetAsync(null));
    }

    [Fact]
    public async Task An_unknown_agency_is_not_found()
    {
        var (service, _) = Create(99);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetAsync(new UpdateAgencyBankRequest("VCB", "0011001234")));
    }
}
