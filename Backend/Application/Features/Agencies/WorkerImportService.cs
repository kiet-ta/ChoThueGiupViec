using CommonService.Application.Exceptions;
using CommonService.Application.Features.Identity;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>One row of the import file after validation.</summary>
public sealed record ImportRow(int RowNumber, string FullName, string PhoneNumber, string NationalId, string? BankName, string? BankAccountNo);

/// <summary>A problem of one row: the 1-based sheet row (the header is row 1), the column header and a short code.</summary>
public sealed record ImportRowError(int Row, string Column, string Code, string Message);

/// <summary>Result of a dry-run or a commit (contract agencies.md 4, decisions Q18).</summary>
/// <param name="Committed">True only for a commit that created every worker.</param>
public sealed record ImportResultDto(bool IsValid, bool Committed, int TotalRows, int ValidRows, int CreatedWorkers, IReadOnlyList<ImportRowError> Errors);

/// <summary>Codes in <c>data.code</c> of a 409/422 and the row error codes.</summary>
public static class ImportErrorCodes
{
    public const string GuaranteeRequired = "GUARANTEE_REQUIRED";
    public const string ImportInvalid = "IMPORT_INVALID";
    public const string InvalidFile = "IMPORT_FILE_INVALID";

    public const string MissingColumn = "MISSING_COLUMN";
    public const string Required = "REQUIRED";
    public const string TooLong = "TOO_LONG";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string DuplicateInFile = "DUPLICATE_IN_FILE";
    public const string AlreadyExists = "ALREADY_EXISTS";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
}

/// <summary>Persistence of the import (BE-M5-03).</summary>
public interface IWorkerImportRepository
{
    /// <summary>The agency's <c>guarantee_signed_at</c>; null when it was never signed (or the agency does not exist).</summary>
    Task<DateTime?> GetGuaranteeSignedAtAsync(int agencyId, CancellationToken cancellationToken = default);

    /// <summary>Stores the signed guarantee (decisions Q19, SC-1); the unit of work saves.</summary>
    Task<bool> SetGuaranteeAsync(int agencyId, DateTime signedAtUtc, string fileUrl, CancellationToken cancellationToken = default);

    /// <summary>The phone numbers (<c>0XXXXXXXXX</c>) among <paramref name="phones"/> that already belong to a worker.</summary>
    Task<IReadOnlySet<string>> FindExistingPhonesAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken = default);

    /// <summary>The national ids among <paramref name="nationalIds"/> that already belong to a worker.</summary>
    Task<IReadOnlySet<string>> FindExistingNationalIdsAsync(IReadOnlyCollection<string> nationalIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the workers and saves them in the caller's transaction. False, with none added, when a phone number or a national id was taken
    /// in the meantime (the unique indexes of WORKER).
    /// </summary>
    Task<bool> TryAddWorkersAsync(IReadOnlyCollection<Worker> workers, CancellationToken cancellationToken = default);
}

/// <summary>
/// Agency worker import (BE-M5-03, decisions Q18, Q19): a downloadable <c>.xlsx</c> template, a dry-run that reports every row error and
/// writes nothing, and an all-or-nothing commit. The guarantee must be signed first (Q19); the whole file is rejected for any invalid
/// row or when it would exceed the worker quota (Q08). Workers are created <c>AGENCY_STAFF</c>, <c>IDLE</c>, with no Admin queue (PRD 2.8).
/// </summary>
public sealed class WorkerImportService(
    IWorkerImportRepository repository,
    IWorkerQuotaGuard quota,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<WorkerImportService>? logger = null)
{
    /// <summary>The template columns, in order: the existing <c>WORKER</c> column names (decisions Q18).</summary>
    public static readonly IReadOnlyList<string> Columns = ["full_name", "phone_number", "national_id", "bank_name", "bank_account_no"];

    // Column sizes of WORKER (WorkerConfiguration): full_name 100, national_id 12, bank_name 100, bank_account_no 30.
    private const int FullNameMax = 100;
    private const int BankNameMax = 100;
    private const int BankAccountMax = 30;
    private const int NationalIdLength = 12;

    /// <summary>provenance: a signed guarantee scan is a few pages; this bounds the upload (decisions Q19).</summary>
    private const long MaxGuaranteeBytes = 10 * 1024 * 1024;

    private readonly ILogger<WorkerImportService> _logger = logger ?? NullLogger<WorkerImportService>.Instance;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    /// <summary>The downloadable template: the header row and one example row.</summary>
    public static byte[] BuildTemplate() => XlsxWriter.Build(
        "workers",
        Columns,
        [new object?[] { "Nguyen Van A", "0901234567", "012345678901", "Vietcombank", "0123456789" }]);

    /// <summary>
    /// Records the signed guarantee: stores the PDF and sets <c>guarantee_signed_at</c> / <c>guarantee_file_url</c> (decisions Q19, SC-1).
    /// The file name carries the document version, so a re-upload never overwrites an earlier one.
    /// </summary>
    public async Task<DateTime> SignGuaranteeAsync(Stream pdf, string fileName, long length, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        if (pdf is null || length <= 0 || length > MaxGuaranteeBytes)
        {
            throw new ValidationException("file", $"The guarantee must be a PDF of at most {MaxGuaranteeBytes / (1024 * 1024)} MB.");
        }

        var header = new byte[5];
        var read = await pdf.ReadAsync(header, cancellationToken);
        if (read < 5 || header[0] != '%' || header[1] != 'P' || header[2] != 'D' || header[3] != 'F' || header[4] != '-')
        {
            throw new ValidationException("file", "The guarantee must be a PDF file.");
        }

        pdf.Position = 0;
        var now = clock.UtcNow;
        var versioned = $"guarantee-v{now:yyyyMMddHHmmss}.pdf";
        var stored = await storage.SaveAsync($"agencies/{agencyId}/guarantee", versioned, pdf, "application/pdf", cancellationToken);

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await repository.SetGuaranteeAsync(agencyId, now, stored.Url, cancellationToken))
            {
                throw new NotFoundException("Agency", agencyId);
            }

            return true;
        }, cancellationToken);
        _logger.LogInformation("Agency {AgencyId} signed its guarantee ({File}).", agencyId, versioned);
        return now;
    }

    public Task<ImportResultDto> DryRunAsync(Stream file, CancellationToken cancellationToken = default) =>
        ProcessAsync(file, commit: false, cancellationToken);

    public Task<ImportResultDto> CommitAsync(Stream file, CancellationToken cancellationToken = default) =>
        ProcessAsync(file, commit: true, cancellationToken);

    private async Task<ImportResultDto> ProcessAsync(Stream file, bool commit, CancellationToken cancellationToken)
    {
        var agencyId = AgencyId;
        if (await repository.GetGuaranteeSignedAtAsync(agencyId, cancellationToken) is null)
        {
            throw new BusinessRuleViolationException(
                "The signed guarantee must be uploaded before workers can be imported.", ImportErrorCodes.GuaranteeRequired);
        }

        IReadOnlyList<IReadOnlyList<string>> sheet;
        try
        {
            sheet = XlsxReader.ReadFirstSheet(file);
        }
        catch (FormatException ex)
        {
            throw new ValidationException("file", ex.Message);
        }

        var (rows, errors) = Parse(sheet);

        // Rows that are well formed are then checked against the database and the quota.
        if (rows.Count > 0)
        {
            var phones = await repository.FindExistingPhonesAsync(rows.Select(r => r.PhoneNumber).ToList(), cancellationToken);
            var ids = await repository.FindExistingNationalIdsAsync(rows.Select(r => r.NationalId).ToList(), cancellationToken);
            foreach (var row in rows)
            {
                if (phones.Contains(row.PhoneNumber))
                {
                    errors.Add(new ImportRowError(row.RowNumber, "phone_number", ImportErrorCodes.AlreadyExists, "This phone number already belongs to a worker."));
                }

                if (ids.Contains(row.NationalId))
                {
                    errors.Add(new ImportRowError(row.RowNumber, "national_id", ImportErrorCodes.AlreadyExists, "This national id already belongs to a worker."));
                }
            }
        }

        var totalRows = Math.Max(0, sheet.Count - 1);
        var invalidRowNumbers = errors.Select(e => e.Row).Where(r => r > 0).Distinct().Count();

        if (errors.Count == 0 && rows.Count > 0)
        {
            try
            {
                await quota.EnsureCanAddWorkersAsync(agencyId, rows.Count, cancellationToken);
            }
            catch (BusinessRuleViolationException ex) when (ex.Code == SubscriptionErrorCodes.QuotaExceeded)
            {
                errors.Add(new ImportRowError(0, string.Empty, ImportErrorCodes.QuotaExceeded, ex.Message));
            }
        }

        var isValid = errors.Count == 0 && rows.Count > 0;
        if (rows.Count == 0 && errors.Count == 0)
        {
            errors.Add(new ImportRowError(0, string.Empty, ImportErrorCodes.Required, "The file has no worker rows."));
        }

        var ordered = errors.OrderBy(e => e.Row).ThenBy(e => e.Column, StringComparer.Ordinal).ToList();
        if (!commit)
        {
            return new ImportResultDto(isValid, Committed: false, totalRows, totalRows - invalidRowNumbers, 0, ordered);
        }

        if (!isValid)
        {
            throw new ImportRejectedException(new ImportResultDto(false, false, totalRows, totalRows - invalidRowNumbers, 0, ordered));
        }

        var now = clock.UtcNow;
        var workers = rows.Select(r =>
        {
            var worker = Worker.CreateAgencyStaff(agencyId, r.PhoneNumber, r.NationalId, r.FullName);
            worker.BankName = r.BankName;
            worker.BankAccountNo = r.BankAccountNo;
            worker.CreatedAt = now;
            worker.UpdatedAt = now;
            return worker;
        }).ToList();

        var created = await unitOfWork.ExecuteInTransactionAsync(
            () => repository.TryAddWorkersAsync(workers, cancellationToken), cancellationToken);
        if (!created)
        {
            // Someone registered one of these numbers between the check and the write: nothing was created.
            throw new ImportRejectedException(new ImportResultDto(
                false, false, totalRows, 0, 0,
                [new ImportRowError(0, string.Empty, ImportErrorCodes.AlreadyExists, "A phone number or national id of the file was taken at the same time.")]));
        }

        _logger.LogInformation("Agency {AgencyId} imported {Count} workers.", agencyId, rows.Count);
        return new ImportResultDto(true, Committed: true, totalRows, totalRows, rows.Count, []);
    }

    /// <summary>Pure: turns the sheet into valid rows plus the errors of every problem row (reported together, never one at a time).</summary>
    public static (List<ImportRow> Rows, List<ImportRowError> Errors) Parse(IReadOnlyList<IReadOnlyList<string>> sheet)
    {
        var errors = new List<ImportRowError>();
        var rows = new List<ImportRow>();
        if (sheet.Count == 0)
        {
            return (rows, errors);
        }

        var header = sheet[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var index = new Dictionary<string, int>();
        foreach (var column in Columns)
        {
            var at = header.IndexOf(column);
            if (at < 0 && column is "full_name" or "phone_number" or "national_id")
            {
                errors.Add(new ImportRowError(1, column, ImportErrorCodes.MissingColumn, $"The header has no column '{column}'."));
            }

            index[column] = at;
        }

        if (errors.Count > 0)
        {
            return (rows, errors);
        }

        var seenPhones = new Dictionary<string, int>();
        var seenIds = new Dictionary<string, int>();

        for (var i = 1; i < sheet.Count; i++)
        {
            var cells = sheet[i];
            var rowNumber = i + 1;

            string Cell(string column) =>
                index[column] >= 0 && index[column] < cells.Count ? cells[index[column]].Trim() : string.Empty;

            if (Columns.All(c => Cell(c).Length == 0))
            {
                continue; // a blank row is not a worker
            }

            var before = errors.Count;
            var fullName = Cell("full_name");
            if (fullName.Length == 0)
            {
                errors.Add(new ImportRowError(rowNumber, "full_name", ImportErrorCodes.Required, "full_name is required."));
            }
            else if (fullName.Length > FullNameMax)
            {
                errors.Add(new ImportRowError(rowNumber, "full_name", ImportErrorCodes.TooLong, $"full_name is at most {FullNameMax} characters."));
            }

            var (phoneOk, phone) = PhoneValidator.TryNormalize(Cell("phone_number"));
            if (Cell("phone_number").Length == 0)
            {
                errors.Add(new ImportRowError(rowNumber, "phone_number", ImportErrorCodes.Required, "phone_number is required."));
            }
            else if (!phoneOk)
            {
                errors.Add(new ImportRowError(rowNumber, "phone_number", ImportErrorCodes.InvalidFormat, "phone_number is not a Vietnamese mobile number."));
            }
            else if (seenPhones.TryGetValue(phone, out var firstPhoneRow))
            {
                errors.Add(new ImportRowError(rowNumber, "phone_number", ImportErrorCodes.DuplicateInFile, $"The phone number repeats row {firstPhoneRow}."));
            }
            else
            {
                seenPhones[phone] = rowNumber;
            }

            var nationalId = Cell("national_id");
            if (nationalId.Length == 0)
            {
                errors.Add(new ImportRowError(rowNumber, "national_id", ImportErrorCodes.Required, "national_id is required."));
            }
            else if (nationalId.Length != NationalIdLength || !nationalId.All(char.IsAsciiDigit))
            {
                errors.Add(new ImportRowError(rowNumber, "national_id", ImportErrorCodes.InvalidFormat, $"national_id must be {NationalIdLength} digits."));
            }
            else if (seenIds.TryGetValue(nationalId, out var firstIdRow))
            {
                errors.Add(new ImportRowError(rowNumber, "national_id", ImportErrorCodes.DuplicateInFile, $"The national id repeats row {firstIdRow}."));
            }
            else
            {
                seenIds[nationalId] = rowNumber;
            }

            var bankName = Cell("bank_name");
            var bankAccount = Cell("bank_account_no");
            if (bankName.Length > BankNameMax)
            {
                errors.Add(new ImportRowError(rowNumber, "bank_name", ImportErrorCodes.TooLong, $"bank_name is at most {BankNameMax} characters."));
            }

            if (bankAccount.Length > BankAccountMax)
            {
                errors.Add(new ImportRowError(rowNumber, "bank_account_no", ImportErrorCodes.TooLong, $"bank_account_no is at most {BankAccountMax} characters."));
            }

            if (errors.Count == before)
            {
                rows.Add(new ImportRow(rowNumber, fullName, phone, nationalId,
                    bankName.Length == 0 ? null : bankName, bankAccount.Length == 0 ? null : bankAccount));
            }
        }

        return (rows, errors);
    }
}

/// <summary>A commit was rejected: carries the per-row errors so the API answers 422 with them (decisions Q18).</summary>
public sealed class ImportRejectedException(ImportResultDto result)
    : BusinessRuleViolationException("The import was rejected: no worker was created.", ImportErrorCodes.ImportInvalid)
{
    public ImportResultDto Result { get; } = result;
}
