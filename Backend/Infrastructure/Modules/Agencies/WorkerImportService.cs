using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Application.Interfaces.Ports;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommonService.Infrastructure.Modules.Agencies
{
    public class WorkerImportService : IWorkerImportService
    {
        private readonly AppDbContext _context;
        private readonly IClock _clock;

        public WorkerImportService(AppDbContext context, IClock clock)
        {
            _context = context;
            _clock = clock;
        }

        public async Task<WorkerImportResult> ImportWorkersAsync(Stream csvStream, int agencyId, CancellationToken cancellationToken = default)
        {
            var result = new WorkerImportResult();
            var now = _clock.UtcNow;

            // Validate agency guarantee
            var agencyHasGuarantee = await _context.PartnerAgencies
                .AnyAsync(pa => pa.AgencyId == agencyId && pa.GuaranteeSignedAt.HasValue, cancellationToken);
            if (!agencyHasGuarantee)
            {
                result.Success = false;
                result.Errors.Add("Agency must have signed guarantee before importing workers.");
                return result;
            }

            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            // Read all lines
            var lines = await reader.ReadToEndAsync(cancellationToken);
            var lineList = lines.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (lineList.Length == 0)
            {
                result.Success = false;
                result.Errors.Add("CSV file is empty.");
                return result;
            }

            // Assume first line is header
            var headerLine = lineList[0].Trim();
            var expectedHeaders = new[] { "PhoneNumber", "NationalId", "FullName" };
            var actualHeaders = headerLine.Split(',').Select(h => h.Trim()).ToArray();
            // We could validate headers, but for simplicity we assume they are correct and in order.
            // We'll just use indices 0,1,2.

            var errors = new List<string>();
            var workersToCreate = new List<Worker>();

            // Check for duplicates within the import
            var seenPhoneNumbers = new HashSet<string>();
            var seenNationalIds = new HashSet<string>();

            // Process each data line
            for (int i = 1; i < lineList.Length; i++)
            {
                var line = lineList[i].Trim();
                if (line.Length == 0)
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 3)
                {
                    errors.Add($"Line {i + 1}: Invalid number of columns. Expected at least 3.");
                    continue;
                }

                var phone = parts[0].Trim();
                var nationalId = parts[1].Trim();
                var fullName = parts[2].Trim();

                var lineNumber = i + 1; // 1-based line number in file (including header)

                if (string.IsNullOrWhiteSpace(phone))
                {
                    errors.Add($"Line {lineNumber}: Phone number is required.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(nationalId))
                {
                    errors.Add($"Line {lineNumber}: National ID is required.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    errors.Add($"Line {lineNumber}: Full name is required.");
                    continue;
                }

                // Validate phone number format (simple: digits and maybe +, -, spaces, length 10-15)
                if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^[\d\s\-\(\)\+]{10,15}$"))
                {
                    errors.Add($"Line {lineNumber}: Invalid phone number format.");
                    continue;
                }

                // Validate national ID format (assuming 12 digits)
                if (!System.Text.RegularExpressions.Regex.IsMatch(nationalId, @"^\d{12}$"))
                {
                    errors.Add($"Line {lineNumber}: National ID must be 12 digits.");
                    continue;
                }

                if (seenPhoneNumbers.Contains(phone))
                {
                    errors.Add($"Line {lineNumber}: Duplicate phone number within import file.");
                    continue;
                }
                if (seenNationalIds.Contains(nationalId))
                {
                    errors.Add($"Line {lineNumber}: Duplicate national ID within import file.");
                    continue;
                }

                // Check against existing workers
                var phoneExists = await _context.Workers
                    .AnyAsync(w => w.PhoneNumber == phone, cancellationToken);
                if (phoneExists)
                {
                    errors.Add($"Line {lineNumber}: Phone number already exists.");
                    continue;
                }
                var nationalIdExists = await _context.Workers
                    .AnyAsync(w => w.NationalId == nationalId, cancellationToken);
                if (nationalIdExists)
                {
                    errors.Add($"Line {lineNumber}: National ID already exists.");
                    continue;
                }

                seenPhoneNumbers.Add(phone);
                seenNationalIds.Add(nationalId);

                // Create worker using factory method
                var worker = Worker.CreateAgencyStaff(agencyId, phone, nationalId, fullName);
                worker.KycStatus = "PENDING"; // or maybe empty string? We'll set pending.
                worker.CreatedAt = now;
                worker.UpdatedAt = now;
                // Other fields have defaults: RatingAvg=0, CompletedJobs=0, EkycConfidence=null, etc.

                workersToCreate.Add(worker);
            }

            if (errors.Any())
            {
                result.Success = false;
                result.Errors = errors;
                return result;
            }

            // Use transaction for atomicity
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _context.Workers.AddRange(workersToCreate);
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                result.Success = true;
                result.WorkersCreated = workersToCreate.Count;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                result.Success = false;
                result.Errors = new List<string> { $"Import failed: {ex.Message}" };
            }

            return result;
        }
    }
}