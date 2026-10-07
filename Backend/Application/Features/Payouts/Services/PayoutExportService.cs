using System.Globalization;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts.Services;

/// <summary>A generated export file, ready to be sent as the response.</summary>
public sealed record PayoutExportFile(byte[] Content, string FileName, string ContentType);

public interface IPayoutExportService
{
    /// <summary>Builds one of the three bank transfer files of a batch (contract payouts.md 2.3, decision Q18).</summary>
    Task<PayoutResult<PayoutExportFile>> ExportAsync(int batchId, string? type, CancellationToken cancellationToken = default);
}

/// <summary>
/// The bank transfer files of a payout batch as <c>.xlsx</c> (BE-M6-05). Columns: Q18 for the freelancer file, the recommended defaults
/// of question P5 for the agency files. The system calls no bank (G-1): the Admin uploads the file to the bank by hand.
/// </summary>
public sealed class PayoutExportService(IPayoutRepository payouts, IFileStorage storage, IClock clock) : IPayoutExportService
{
    public const string TypeFreelancer = "freelancer";
    public const string TypeAgencySummary = "agency-summary";
    public const string TypeAgencyDetail = "agency-detail";

    public static readonly IReadOnlyList<string> Types = [TypeFreelancer, TypeAgencySummary, TypeAgencyDetail];

    private const string StorageFolder = "payouts"; // LocalFileStorage keeps one folder level
    private const string FilesUrlPrefix = "/files/";

    public static readonly IReadOnlyList<string> FreelancerHeaders =
        ["full_name", "bank_name", "bank_account_no", "net_amount", "transfer_note"];

    public static readonly IReadOnlyList<string> AgencySummaryHeaders =
        ["agency_name", "bank_name", "bank_account_no", "job_count", "gross_amount", "commission_amount", "penalty_amount", "net_amount", "transfer_note"];

    public static readonly IReadOnlyList<string> AgencyDetailHeaders =
        ["agency_name", "assignment_id", "order_id", "completed_at", "worker_name", "gross_amount", "commission_amount", "net_amount"];

    public async Task<PayoutResult<PayoutExportFile>> ExportAsync(int batchId, string? type, CancellationToken cancellationToken = default)
    {
        var wanted = type?.Trim().ToLowerInvariant();
        if (wanted is null || !Types.Contains(wanted))
        {
            return PayoutResult<PayoutExportFile>.ValidationError(new Dictionary<string, string[]>
            {
                ["type"] = [$"type must be one of: {string.Join(", ", Types)}."],
            });
        }

        var batch = await payouts.GetBatchAsync(batchId, cancellationToken);
        if (batch is null) return PayoutResult<PayoutExportFile>.NotFound();

        var note = $"Thanh toan ChoThueGiupViec {batch.PeriodMonth}"; // ASCII: banks reject accents in the transfer note
        byte[] content;
        switch (wanted)
        {
            case TypeFreelancer:
                {
                    var items = await payouts.GetAllItemsAsync(batchId, PayeeType.Freelancer, cancellationToken);
                    var rows = items
                        .Where(v => v.Item.NetAmount > 0) // nothing to transfer when a penalty took the whole month
                        .Select(v => (IReadOnlyList<object?>)[v.PayeeName, v.BankName, v.Item.BankAccountNo, v.Item.NetAmount, note])
                        .ToList();
                    content = XlsxWriter.Build("Freelancer", FreelancerHeaders, rows);
                    break;
                }

            case TypeAgencySummary:
                {
                    var items = await payouts.GetAllItemsAsync(batchId, PayeeType.Agency, cancellationToken);
                    var rows = items
                        .Where(v => v.Item.NetAmount > 0)
                        .Select(v => (IReadOnlyList<object?>)
                        [
                            v.PayeeName, v.BankName, v.Item.BankAccountNo, v.Item.JobCount, v.Item.GrossAmount,
                        v.Item.CommissionAmount, v.Item.PenaltyAmount, v.Item.NetAmount, note,
                        ])
                        .ToList();
                    content = XlsxWriter.Build("Agency summary", AgencySummaryHeaders, rows);
                    break;
                }

            default:
                {
                    var details = await payouts.GetAgencyDetailAsync(batchId, cancellationToken);
                    var rows = details.Select(d =>
                    {
                        var amounts = PayoutCalculator.AmountsOf(new PayoutAssignmentRow(
                            d.AssignmentId, 0, null, d.Status, d.GrossAmount, d.CommissionRate, d.AbsenceFeeAmount));
                        return (IReadOnlyList<object?>)
                        [
                            d.AgencyName, d.AssignmentId, d.OrderId, LocalTime(d.DateUtc), d.WorkerName,
                        amounts.Gross, amounts.Commission, amounts.Payable,
                        ];
                    }).ToList();
                    content = XlsxWriter.Build("Agency detail", AgencyDetailHeaders, rows);
                    break;
                }
        }

        var fileName = $"payout-{batch.PeriodMonth}-{wanted}.xlsx";
        await StoreAsync(batch.BatchId, batch.ExportFileUrl, fileName, content, cancellationToken);
        return PayoutResult<PayoutExportFile>.Ok(new PayoutExportFile(content, fileName, XlsxWriter.ContentType));
    }

    /// <summary>
    /// Keeps the file through <see cref="IFileStorage"/> and writes its url to <c>export_file_url</c>; the file stored by the previous
    /// export of the same batch is deleted so exports do not pile up (the column holds the latest one).
    /// </summary>
    private async Task StoreAsync(int batchId, string? previousUrl, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        var stored = await storage.SaveAsync(StorageFolder, fileName, stream, XlsxWriter.ContentType, cancellationToken);
        await payouts.SetExportUrlAsync(batchId, stored.Url, cancellationToken);

        if (previousUrl is not null && previousUrl.StartsWith(FilesUrlPrefix, StringComparison.Ordinal))
        {
            await storage.DeleteAsync(previousUrl[FilesUrlPrefix.Length..], cancellationToken);
        }
    }

    /// <summary>ISO-8601 in Asia/Ho_Chi_Minh with the offset, so the Admin reads local time without ambiguity (decision G-3).</summary>
    private string LocalTime(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = DateTime.SpecifyKind(clock.ToLocal(asUtc), DateTimeKind.Unspecified);
        var offset = local - DateTime.SpecifyKind(asUtc, DateTimeKind.Unspecified); // +07:00 for Ho Chi Minh
        return new DateTimeOffset(local, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
