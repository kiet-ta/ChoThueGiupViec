using System.Reflection;
using CommonService.Application;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Agencies.Handlers;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.WebAPI.Controllers.Agencies;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>
/// BE-M5-01 use case: handler with a stub repository (no database, Golden Rule), the MediatR pipeline with the module
/// wiring, and the controller contract. Uses the real PBKDF2 hasher: it is pure CPU, no I/O.
/// </summary>
public class RegisterAgencyCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utc) : IClock
    {
        public DateTime UtcNow => utc;
        public DateTime ToLocal(DateTime value) => value.AddHours(7);
        public DateTime ToUtc(DateTime value) => value.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utc));
    }

    private sealed class StubAgencyRepository : IAgencyRepository
    {
        public List<PartnerAgency> Rows { get; } = [];
        public List<Worker> WorkerRows { get; } = [];
        public int AddCalls { get; private set; }
        public Exception? FailOnAdd { get; set; }

        public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Any(a => a.TaxCode == taxCode));

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Any(a => a.ContactEmail == email));

        public Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            if (FailOnAdd is not null)
            {
                throw FailOnAdd;
            }

            agency.AgencyId = Rows.Count + 1;
            Rows.Add(agency);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<AgencyWorkerItem> Items, int TotalCount)> GetAgencyWorkersAsync(
            int agencyId, int page, int pageSize, string? statusFilter, CancellationToken cancellationToken = default)
        {
            var items = WorkerRows
                .Where(w => w.AgencyId == agencyId)
                .Select(w => new AgencyWorkerItem(w.WorkerId, w.FullName, w.WorkStatus.ToString().ToUpperInvariant(), 0))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(((IReadOnlyList<AgencyWorkerItem>)items, WorkerRows.Count));
        }

        public Task<Worker?> GetAgencyWorkerAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkerRows.FirstOrDefault(w => w.WorkerId == workerId && w.AgencyId == agencyId));

        public Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<FutureJobHandover>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FutureJobHandover>>([]);

        public Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var worker = Worker.CreateAgencyStaff(agencyId, details.PhoneNumber!, details.NationalId!, details.FullName!);
            worker.BankName = details.BankName;
            worker.BankAccountNo = details.BankAccountNo;
            worker.CreatedAt = nowUtc;
            worker.UpdatedAt = nowUtc;
            worker.WorkerId = WorkerRows.Count + 1;
            WorkerRows.Add(worker);
            return Task.FromResult(worker);
        }

        public Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkerRows.Any(w => w.PhoneNumber == phoneNumber || w.NationalId == nationalId));

        public Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<Worker>> GetRetiringWorkersReadyForRetiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Worker>>([]);

        public Task<bool> HasActiveOrFutureJobsAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static RegisterAgencyRequest ValidRequest() => new(
        "0312345678", "Cong ty TNHH Don Dep ABC", "Nguyen Van A", "0901234567", "Partner@Example.com", "Abcdefgh12");

    private static BusinessRules Rules(Action<BusinessRules>? change = null)
    {
        var rules = new BusinessRules();
        change?.Invoke(rules);
        return rules;
    }

    private static RegisterAgencyCommandHandler Handler(StubAgencyRepository repo, BusinessRules? rules = null) =>
        new(repo, new Pbkdf2PasswordHasher(), new FixedClock(Now), Microsoft.Extensions.Options.Options.Create(rules ?? new BusinessRules()));

    // ---- handler ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registration_creates_a_pending_agency_with_zero_escrow_and_no_quota()
    {
        var repo = new StubAgencyRepository();

        var result = await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var row = Assert.Single(repo.Rows);
        Assert.Equal("PENDING", row.AgencyStatus);
        Assert.Equal(0m, row.EscrowDepositBalance);
        Assert.Equal(100.00m, row.SlaScore);
        Assert.Equal(0, row.WorkerQuota);
        Assert.False(row.IsVerifiedPartner);
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(0, row.FailedLoginCount);
        Assert.Null(row.LockedUntil);
        Assert.Null(row.GuaranteeSignedAt);
        Assert.True(result.Success);
        Assert.Equal("PENDING", result.Data!.AgencyStatus);
    }

    [Fact]
    public async Task Stored_fields_are_trimmed_and_normalized()
    {
        var repo = new StubAgencyRepository();
        var request = ValidRequest() with
        {
            TaxCode = " 0312345678-001 ",
            ContactPhone = "+84901234567",
            ContactEmail = " Partner@Example.COM "
        };

        await Handler(repo).Handle(new RegisterAgencyCommand(request), default);

        var row = Assert.Single(repo.Rows);
        Assert.Equal("0312345678-001", row.TaxCode);
        Assert.Equal("0901234567", row.ContactPhone);
        Assert.Equal("partner@example.com", row.ContactEmail);
    }

    [Fact]
    public async Task Initial_sla_score_and_status_come_from_configuration_not_constants()
    {
        var repo = new StubAgencyRepository();
        var rules = Rules(r =>
        {
            r.Sla.InitialScore = 77.50m;
            r.Agency.MinEscrowBalance = 0m;
        });

        await Handler(repo, rules).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var row = Assert.Single(repo.Rows);
        Assert.Equal(77.50m, row.SlaScore);
        Assert.Equal("ACTIVE", row.AgencyStatus); // a minimum of 0 is met by the initial balance of 0
    }

    [Fact]
    public async Task Password_is_stored_only_as_a_verifiable_hash()
    {
        var repo = new StubAgencyRepository();
        var hasher = new Pbkdf2PasswordHasher();

        var result = await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var row = Assert.Single(repo.Rows);
        Assert.NotNull(row.PasswordHash);
        Assert.NotEqual("Abcdefgh12", row.PasswordHash);
        Assert.DoesNotContain("Abcdefgh12", row.PasswordHash!);
        Assert.True(hasher.Verify("Abcdefgh12", row.PasswordHash!));
        Assert.False(hasher.Verify("Abcdefgh13", row.PasswordHash!));

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Abcdefgh12", json);
        Assert.DoesNotContain(row.PasswordHash!, json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Response_describes_the_new_agency()
    {
        var repo = new StubAgencyRepository();

        var result = await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var data = result.Data!;
        Assert.Equal(1, data.AgencyId);
        Assert.Equal("0312345678", data.TaxCode);
        Assert.Equal("Cong ty TNHH Don Dep ABC", data.LegalName);
        Assert.Equal("Nguyen Van A", data.LegalRepresentative);
        Assert.Equal("0901234567", data.ContactPhone);
        Assert.Equal("partner@example.com", data.ContactEmail);
        Assert.Equal(0m, data.EscrowDepositBalance);
        Assert.Equal(0, data.WorkerQuota);
        Assert.Equal(Now, data.CreatedAt);
    }

    [Fact]
    public async Task Duplicate_tax_code_is_a_409_with_a_machine_readable_code()
    {
        var repo = new StubAgencyRepository();
        await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var second = ValidRequest() with { ContactEmail = "other@example.com", TaxCode = " 0312345678 " };
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(second), default));

        Assert.Equal("AGENCY_TAX_CODE_EXISTS", ex.Code);
        Assert.Equal(1, repo.AddCalls);
    }

    [Fact]
    public async Task Duplicate_email_ignores_case_and_surrounding_spaces()
    {
        var repo = new StubAgencyRepository();
        await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var second = ValidRequest() with { TaxCode = "0412345678", ContactEmail = "  PARTNER@example.com " };
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(second), default));

        Assert.Equal("AGENCY_EMAIL_EXISTS", ex.Code);
        Assert.Equal(1, repo.AddCalls);
    }

    [Fact]
    public async Task Tax_code_is_checked_before_email_when_both_exist()
    {
        var repo = new StubAgencyRepository();
        await Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default));

        Assert.Equal("AGENCY_TAX_CODE_EXISTS", ex.Code);
    }

    [Fact]
    public async Task Invalid_input_never_reaches_the_repository_or_the_hasher()
    {
        var repo = new StubAgencyRepository();
        var bad = new RegisterAgencyRequest(new string('9', 100), "", "", "not-a-phone", "not-an-email", "a");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(bad), default));

        Assert.Equal(6, ex.Errors.Count);
        Assert.Equal(0, repo.AddCalls);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task Missing_body_is_a_400_not_a_null_reference()
    {
        var repo = new StubAgencyRepository();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(null), default));

        Assert.Contains("request", ex.Errors.Keys);
    }

    [Fact]
    public async Task Concurrent_duplicate_translated_by_the_repository_surfaces_as_409()
    {
        var repo = new StubAgencyRepository
        {
            FailOnAdd = new BusinessRuleViolationException("dup", AgencyErrorCodes.TaxCodeExists)
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Handler(repo).Handle(new RegisterAgencyCommand(ValidRequest()), default));

        Assert.Equal("AGENCY_TAX_CODE_EXISTS", ex.Code);
    }

    // ---- MediatR pipeline + module wiring --------------------------------------------------------------------

    private static ServiceProvider Pipeline(StubAgencyRepository repo, BusinessRules? rules = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IOptions<BusinessRules>>(Microsoft.Extensions.Options.Options.Create(rules ?? new BusinessRules()));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAgencyRepository>(repo);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Command_is_dispatched_to_the_handler_through_mediatr()
    {
        var repo = new StubAgencyRepository();
        await using var provider = Pipeline(repo);

        var result = await provider.GetRequiredService<ISender>().Send(new RegisterAgencyCommand(ValidRequest()));

        Assert.True(result.Success);
        Assert.Single(repo.Rows);
    }

    [Fact]
    public void Agencies_module_registers_the_ef_repository()
    {
        var services = new ServiceCollection();

        new AgenciesModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IAgencyRepository));
        Assert.Equal(typeof(EfAgencyRepository), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal("Agencies", new AgenciesModule().Name);
    }

    // ---- controller ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Controller_returns_201_with_the_response_envelope()
    {
        var repo = new StubAgencyRepository();
        await using var provider = Pipeline(repo);
        var controller = new AgenciesController(provider.GetRequiredService<ISender>());

        var action = await controller.Register(ValidRequest(), default);

        var created = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var envelope = Assert.IsType<ApiResponse<RegisterAgencyResponse>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Equal("PENDING", envelope.Data!.AgencyStatus);
    }

    [Fact]
    public void Registration_route_is_public_and_not_guarded_by_a_policy()
    {
        var method = typeof(AgenciesController).GetMethod(nameof(AgenciesController.Register))!;

        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(AgenciesController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/agencies", typeof(AgenciesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("registration", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Request_and_response_contracts_have_no_business_registration_certificate_field()
    {
        // Leader decision (issue #237): taxCode is the ĐKKD/MST identity; there is no separate column or field.
        var names = typeof(RegisterAgencyRequest).GetProperties().Select(p => p.Name)
            .Concat(typeof(RegisterAgencyResponse).GetProperties().Select(p => p.Name))
            .Concat(typeof(PartnerAgency).GetProperties().Select(p => p.Name));

        Assert.DoesNotContain(names, n => n.Contains("BusinessRegistration", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            // BankAccountNo / BankName: the optional payout account (MAIN-FLOW-FIX, decisions D18).
            new[] { "BankAccountNo", "BankName", "ContactEmail", "ContactPhone", "LegalName", "LegalRepresentative", "Password", "TaxCode" },
            typeof(RegisterAgencyRequest).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }
}
