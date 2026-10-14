using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Payouts;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-03: the worker import (decisions Q18 dry-run + all-or-nothing commit, Q19 guarantee first, Q08 quota).</summary>
public class WorkerImportTests
{
    private static byte[] Workbook(params object?[][] rows) =>
        XlsxWriter.Build("workers", WorkerImportService.Columns, rows.Select(r => (IReadOnlyList<object?>)r).ToList());

    private static MemoryStream Stream(byte[] bytes) => new(bytes);

    // ---- the reader

    [Fact]
    public void Reader_round_trips_what_the_writer_wrote_including_leading_zeros()
    {
        var bytes = Workbook(["Nguyen Van A", "0901234567", "012345678901", "Vietcombank", "0123456789"], ["Tran B", "0912345678", "123456789012", null, null]);

        var sheet = XlsxReader.ReadFirstSheet(Stream(bytes));

        Assert.Equal(3, sheet.Count);
        Assert.Equal(WorkerImportService.Columns, sheet[0]);
        Assert.Equal("0901234567", sheet[1][1]); // the leading zero survives
        Assert.Equal("012345678901", sheet[1][2]);
        Assert.Equal("Tran B", sheet[2][0]);
    }

    [Fact]
    public void Reader_rejects_something_that_is_not_a_workbook()
    {
        Assert.Throws<FormatException>(() => XlsxReader.ReadFirstSheet(Stream([1, 2, 3, 4, 5])));
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("B7", 1)]
    [InlineData("Z1", 25)]
    [InlineData("AA3", 26)]
    [InlineData(null, 0)]
    public void Column_reference_is_a_zero_based_index(string? reference, int expected) =>
        Assert.Equal(expected, XlsxReader.ColumnIndex(reference));

    // ---- the row rules

    private static IReadOnlyList<IReadOnlyList<string>> Sheet(params string[][] rows) =>
        [WorkerImportService.Columns.ToArray(), .. rows];

    [Fact]
    public void A_good_row_is_normalised()
    {
        var (rows, errors) = WorkerImportService.Parse(Sheet(["  Nguyen Van A ", "+84 901 234 567", "012345678901", "", ""]));

        Assert.Empty(errors);
        var row = Assert.Single(rows);
        Assert.Equal("Nguyen Van A", row.FullName);
        Assert.Equal("0901234567", row.PhoneNumber);
        Assert.Null(row.BankName);
        Assert.Equal(2, row.RowNumber);
    }

    [Fact]
    public void Every_problem_of_every_row_is_reported_together_with_its_sheet_row()
    {
        var (rows, errors) = WorkerImportService.Parse(Sheet(
            ["", "0901234567", "012345678901", "", ""], // row 2: no name
            ["Tran B", "12345", "012345678901", "", ""], // row 3: bad phone
            ["Le C", "0912345678", "12345", "", ""], // row 4: bad national id
            ["Pham D", "0923456789", "223456789012", new string('x', 101), ""], // row 5: bank name too long
            ["Hoang E", "0901234567", "323456789012", "", ""])); // row 6: phone repeats row 2

        Assert.DoesNotContain(rows, r => r.RowNumber is 2 or 3 or 4 or 5 or 6); // every row of this file has a problem
        Assert.Contains(errors, e => e.Row == 2 && e.Column == "full_name" && e.Code == ImportErrorCodes.Required);
        Assert.Contains(errors, e => e.Row == 3 && e.Column == "phone_number" && e.Code == ImportErrorCodes.InvalidFormat);
        Assert.Contains(errors, e => e.Row == 4 && e.Column == "national_id" && e.Code == ImportErrorCodes.InvalidFormat);
        Assert.Contains(errors, e => e.Row == 5 && e.Column == "bank_name" && e.Code == ImportErrorCodes.TooLong);
        // A phone that appears twice is flagged on the later row even when the first row has another problem.
        Assert.Contains(errors, e => e.Row == 6 && e.Column == "phone_number" && e.Code == ImportErrorCodes.DuplicateInFile);
    }

    [Fact]
    public void A_phone_or_national_id_repeated_in_the_file_is_flagged_on_the_second_row()
    {
        var (rows, errors) = WorkerImportService.Parse(Sheet(
            ["A", "0901234567", "012345678901", "", ""],
            ["B", "0901234567", "112345678901", "", ""],
            ["C", "0912345678", "012345678901", "", ""]));

        Assert.Single(rows);
        Assert.Contains(errors, e => e.Row == 3 && e.Code == ImportErrorCodes.DuplicateInFile && e.Column == "phone_number");
        Assert.Contains(errors, e => e.Row == 4 && e.Code == ImportErrorCodes.DuplicateInFile && e.Column == "national_id");
    }

    [Fact]
    public void Blank_rows_are_skipped_and_a_missing_required_column_is_reported_once()
    {
        var (rows, errors) = WorkerImportService.Parse(Sheet(["", "", "", "", ""], ["A", "0901234567", "012345678901", "", ""]));
        Assert.Single(rows);
        Assert.Empty(errors);

        var (noRows, headerErrors) = WorkerImportService.Parse([["full_name", "phone_number"], ["A", "0901234567"]]);
        Assert.Empty(noRows);
        var error = Assert.Single(headerErrors);
        Assert.Equal(ImportErrorCodes.MissingColumn, error.Code);
        Assert.Equal("national_id", error.Column);
    }

    // ---- the service

    internal sealed class InMemoryImport : IWorkerImportRepository
    {
        public DateTime? GuaranteeSignedAt { get; set; }
        public string? GuaranteeUrl { get; private set; }
        public HashSet<string> ExistingPhones { get; } = [];
        public HashSet<string> ExistingIds { get; } = [];
        public List<Worker> Added { get; } = [];

        public Task<DateTime?> GetGuaranteeSignedAtAsync(int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GuaranteeSignedAt);

        public Task<bool> SetGuaranteeAsync(int agencyId, DateTime signedAtUtc, string fileUrl, CancellationToken cancellationToken = default)
        {
            GuaranteeSignedAt = signedAtUtc;
            GuaranteeUrl = fileUrl;
            return Task.FromResult(true);
        }

        public Task<IReadOnlySet<string>> FindExistingPhonesAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(phones.Where(ExistingPhones.Contains).ToHashSet());

        public Task<IReadOnlySet<string>> FindExistingNationalIdsAsync(IReadOnlyCollection<string> nationalIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(nationalIds.Where(ExistingIds.Contains).ToHashSet());

        public bool LoseWrite { get; set; }

        public Task<bool> TryAddWorkersAsync(IReadOnlyCollection<Worker> workers, CancellationToken cancellationToken = default)
        {
            if (LoseWrite)
            {
                return Task.FromResult(false);
            }

            Added.AddRange(workers);
            return Task.FromResult(true);
        }
    }

    private sealed class Quota(int allowed) : IWorkerQuotaGuard
    {
        public Task EnsureCanAddWorkersAsync(int agencyId, int additionalWorkers, CancellationToken cancellationToken = default) =>
            additionalWorkers > allowed
                ? throw new BusinessRuleViolationException("Over the quota.", SubscriptionErrorCodes.QuotaExceeded)
                : Task.CompletedTask;
    }

    internal sealed class PassThroughUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private static (WorkerImportService Service, InMemoryImport Repo) Create(int quota = 50, bool signed = true)
    {
        var repo = new InMemoryImport { GuaranteeSignedAt = signed ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : null };
        var user = new FakeCurrentUser();
        user.SignIn(7, UserRole.Partner);
        var clock = new FakeClock();
        clock.Set(new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));
        var service = new WorkerImportService(
            repo, new Quota(quota), new FakeFileStorage(), new PassThroughUnitOfWork(), user, clock, NullLogger<WorkerImportService>.Instance);
        return (service, repo);
    }

    private static byte[] ThreeGoodRows() => Workbook(
        ["A", "0901234567", "012345678901", "Vietcombank", "0123456789"],
        ["B", "0912345678", "112345678901", null, null],
        ["C", "0923456789", "212345678901", null, null]);

    [Fact]
    public async Task Import_is_refused_until_the_guarantee_is_signed()
    {
        var (service, repo) = Create(signed: false);

        var dry = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.DryRunAsync(Stream(ThreeGoodRows())));
        var commit = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.CommitAsync(Stream(ThreeGoodRows())));

        Assert.Equal(ImportErrorCodes.GuaranteeRequired, dry.Code);
        Assert.Equal(ImportErrorCodes.GuaranteeRequired, commit.Code);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task Dry_run_reports_without_writing_anything()
    {
        var (service, repo) = Create();

        var result = await service.DryRunAsync(Stream(ThreeGoodRows()));

        Assert.True(result.IsValid);
        Assert.False(result.Committed);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(3, result.ValidRows);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task Commit_creates_every_worker_as_idle_agency_staff_linked_to_the_agency()
    {
        var (service, repo) = Create();

        var result = await service.CommitAsync(Stream(ThreeGoodRows()));

        Assert.True(result.Committed);
        Assert.Equal(3, result.CreatedWorkers);
        Assert.Equal(3, repo.Added.Count);
        Assert.All(repo.Added, w =>
        {
            Assert.Equal(WorkerType.AgencyStaff, w.WorkerType);
            Assert.Equal(7, w.AgencyId);
            Assert.Equal(WorkStatus.Idle, w.WorkStatus);
        });
        Assert.Equal("Vietcombank", repo.Added[0].BankName);
        Assert.Null(repo.Added[1].BankName);
    }

    [Fact]
    public async Task A_number_taken_at_the_same_time_rejects_the_commit_with_a_422_style_result()
    {
        var (service, repo) = Create();
        repo.LoseWrite = true;

        var ex = await Assert.ThrowsAsync<ImportRejectedException>(() => service.CommitAsync(Stream(ThreeGoodRows())));

        Assert.Contains(ex.Result.Errors, e => e.Code == ImportErrorCodes.AlreadyExists);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task One_invalid_row_rejects_the_whole_file_and_creates_nobody()
    {
        var (service, repo) = Create();
        var file = Workbook(
            ["A", "0901234567", "012345678901", null, null],
            ["B", "not-a-phone", "112345678901", null, null]);

        var ex = await Assert.ThrowsAsync<ImportRejectedException>(() => service.CommitAsync(Stream(file)));

        Assert.Equal(ImportErrorCodes.ImportInvalid, ex.Code);
        Assert.Contains(ex.Result.Errors, e => e.Row == 3 && e.Column == "phone_number");
        Assert.Equal(1, ex.Result.ValidRows);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task A_worker_that_already_exists_is_an_error_of_its_row()
    {
        var (service, repo) = Create();
        repo.ExistingPhones.Add("0912345678");
        repo.ExistingIds.Add("212345678901");

        var result = await service.DryRunAsync(Stream(ThreeGoodRows()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Row == 3 && e.Code == ImportErrorCodes.AlreadyExists && e.Column == "phone_number");
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Code == ImportErrorCodes.AlreadyExists && e.Column == "national_id");
    }

    [Fact]
    public async Task Exceeding_the_quota_rejects_the_whole_file()
    {
        var (service, repo) = Create(quota: 2);

        var dry = await service.DryRunAsync(Stream(ThreeGoodRows()));
        var ex = await Assert.ThrowsAsync<ImportRejectedException>(() => service.CommitAsync(Stream(ThreeGoodRows())));

        Assert.False(dry.IsValid);
        Assert.Contains(dry.Errors, e => e.Code == ImportErrorCodes.QuotaExceeded);
        Assert.Contains(ex.Result.Errors, e => e.Code == ImportErrorCodes.QuotaExceeded);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task An_empty_file_and_a_non_xlsx_file_are_validation_problems()
    {
        var (service, _) = Create();

        var empty = await service.DryRunAsync(Stream(Workbook()));
        Assert.False(empty.IsValid);
        await Assert.ThrowsAsync<ValidationException>(() => service.DryRunAsync(Stream([1, 2, 3, 4, 5])));
    }

    [Fact]
    public async Task Guarantee_upload_records_the_signature_time_and_url_and_accepts_only_a_pdf()
    {
        var (service, repo) = Create(signed: false);
        var pdf = "%PDF-1.7 fake"u8.ToArray();

        var signedAt = await service.SignGuaranteeAsync(Stream(pdf), "g.pdf", pdf.Length);

        Assert.Equal(signedAt, repo.GuaranteeSignedAt);
        Assert.NotNull(repo.GuaranteeUrl);
        Assert.Contains("guarantee-v", repo.GuaranteeUrl);

        var notPdf = "hello world"u8.ToArray();
        await Assert.ThrowsAsync<ValidationException>(() => service.SignGuaranteeAsync(Stream(notPdf), "g.pdf", notPdf.Length));
        await Assert.ThrowsAsync<ValidationException>(() => service.SignGuaranteeAsync(Stream([]), "g.pdf", 0));
    }

    [Fact]
    public async Task A_non_partner_cannot_import()
    {
        var repo = new InMemoryImport { GuaranteeSignedAt = DateTime.UtcNow };
        var user = new FakeCurrentUser();
        user.SignIn(1, UserRole.Customer);
        var service = new WorkerImportService(
            repo, new Quota(50), new FakeFileStorage(), new PassThroughUnitOfWork(), user, new FakeClock());

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.DryRunAsync(Stream(ThreeGoodRows())));
    }

    [Fact]
    public void The_template_has_the_documented_header_and_one_example()
    {
        var sheet = XlsxReader.ReadFirstSheet(Stream(WorkerImportService.BuildTemplate()));

        Assert.Equal(WorkerImportService.Columns, sheet[0]);
        Assert.Equal(2, sheet.Count);
        var (rows, errors) = WorkerImportService.Parse(sheet);
        Assert.Empty(errors);
        Assert.Single(rows); // the example row is itself a valid row
    }
}
