using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonService.Domain.Enums;
using CommonService.Application.Exceptions;
using CommonService.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Application.Features.Workers.Commands
{
    public class ImportWorkerCommandHandler : IRequestHandler<ImportWorkerCommand, ImportWorkerResult>
    {
        private readonly IWorkerRepository _workerRepository;
        private readonly IAgencyRepository _agencyRepository;
        private readonly ILogger<ImportWorkerCommandHandler> _logger;

        public ImportWorkerCommandHandler(
            IWorkerRepository workerRepository,
            IAgencyRepository agencyRepository,
            ILogger<ImportWorkerCommandHandler> logger)
        {
            _workerRepository = workerRepository;
            _agencyRepository = agencyRepository;
            _logger = logger;
        }

        public async Task<ImportWorkerResult> Handle(ImportWorkerCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting worker import process for agency {AgencyId}", request.AgencyId);

            // Validate agency exists
            var agencyExists = await _agencyRepository.ExistsAsync(request.AgencyId, cancellationToken);
            if (!agencyExists)
            {
                throw new NotFoundException("Agency not found", request.AgencyId);
            }

            var results = new ImportWorkerResult();
            var lineNumber = 0;

            try
            {
                using var reader = new StreamReader(request.File.OpenReadStream());
                // Skip header if present
                if (request.HasHeader)
                {
                    await reader.ReadLineAsync(cancellationToken);
                    lineNumber++;
                }

                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var worker = await ParseAndValidateWorkerLine(line, lineNumber, request.AgencyId, cancellationToken);
                        if (worker != null)
                        {
                            await _workerRepository.AddAsync(worker, cancellationToken);
                            results.SuccessCount++;
                        }
                    }
                    catch (ValidationException ex)
                    {
                        results.Errors.Add(new ImportWorkerError
                        {
                            LineNumber = lineNumber,
                            FieldName = ex.FieldName,
                            Message = ex.Message,
                            RawData = line
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Errors.Add(new ImportWorkerError
                        {
                            LineNumber = lineNumber,
                            FieldName = "Unknown",
                            Message = $"Unexpected error: {ex.Message}",
                            RawData = line
                        });
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Worker import completed. Success: {Success}, Errors: {ErrorCount}",
                    results.SuccessCount, results.Errors.Count);
            }

            return results;
        }

        private async Task<Worker?> ParseAndValidateWorkerLine(
            string line,
            int lineNumber,
            int agencyId,
            CancellationToken cancellationToken)
        {
            var values = ParseCsvLine(line);

            if (values.Length < 5)
            {
                throw new ValidationException("Line", $"Expected at least 5 fields, got {values.Length}");
            }

            var worker = new Worker();

            // Parse and validate phone number (required)
            var phoneNumber = values[0].Trim();
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                throw new ValidationException("PhoneNumber", "Phone number is required");
            }

            if (!IsValidVietnamesePhone(phoneNumber))
            {
                throw new ValidationException("PhoneNumber", $"Phone number '{phoneNumber}' is not a valid Vietnamese format");
            }

            worker.PhoneNumber = phoneNumber;

            // Parse and validate national ID (required)
            var nationalId = values[1].Trim();
            if (string.IsNullOrWhiteSpace(nationalId))
            {
                throw new ValidationException("NationalId", "National ID is required");
            }

            if (nationalId.Length != 12 || !nationalId.All(char.IsDigit))
            {
                throw new ValidationException("NationalId", $"National ID '{nationalId}' must be 12 digits");
            }

            worker.NationalId = nationalId;

            // Parse and validate full name (required)
            var fullName = values[2].Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                throw new ValidationException("FullName", "Full name is required");
            }

            if (fullName.Length > 100)
            {
                throw new ValidationException("FullName", $"Full name exceeds 100 characters");
            }

            worker.FullName = fullName;

            // Parse and validate agency ID (must match requesting agency)
            if (!int.TryParse(values[3].Trim(), out var parsedAgencyId))
            {
                throw new ValidationException("AgencyId", "Agency ID must be a valid integer");
            }

            if (parsedAgencyId != agencyId)
            {
                throw new ValidationException("AgencyId", $"Worker belongs to agency {parsedAgencyId}, but import is for agency {agencyId}");
            }

            worker.AgencyId = agencyId;

            // Parse worker type (optional, defaults to AgencyStaff for this import)
            WorkerType workerType = WorkerType.AgencyStaff; // Default for agency imports
            if (values.Length > 4 && !string.IsNullOrWhiteSpace(values[4]))
            {
                var workerTypeStr = values[4].Trim();
                if (!Enum.TryParse<WorkerType>(workerTypeStr, true, out var parsedWorkerType))
                {
                    throw new ValidationException("WorkerType", $"Worker type '{workerTypeStr}' is not valid");
                }
                workerType = parsedWorkerType;
            }
            worker.WorkerType = workerType;

            // Set worker status to pending (new workers start as pending)
            worker.WorkStatus = WorkStatus.Pending;

            // Set timestamps
            var now = DateTime.UtcNow;
            worker.CreatedAt = now;
            worker.UpdatedAt = now;

            // Set guarantee signed timestamp (required for agency imports)
            worker.GuaranteeSignedAt = now;

            // Check for duplicate phone number
            var phoneExists = await _workerRepository.ExistsByPhoneNumberAsync(phoneNumber, cancellationToken);
            if (phoneExists)
            {
                throw new ValidationException("PhoneNumber", $"Worker with phone number '{phoneNumber}' already exists");
            }

            // Check for duplicate national ID
            var nationalIdExists = await _workerRepository.ExistsByNationalIdAsync(nationalId, cancellationToken);
            if (nationalIdExists)
            {
                throw new ValidationException("NationalId", $"Worker with national ID '{nationalId}' already exists");
            }

            return worker;
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = "";
            var inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                    continue;
                }

                current += c;
            }

            result.Add(current);
            return result.ToArray();
        }

        private bool IsValidVietnamesePhone(string phoneNumber)
        {
            // Remove any spaces, dashes, or parentheses
            var cleaned = new string(phoneNumber.Where(c => char.IsDigit(c)).ToArray());

            // Vietnamese phone numbers: 10 or 11 digits
            // Common prefixes: 09x, 08x, 07x, 05x, 03x
            if (cleaned.Length != 10 && cleaned.Length != 11)
                return false;

            // Must start with 0
            if (!cleaned.StartsWith("0"))
                return false;

            // Second digit should be in valid range for Vietnamese mobile
            if (cleaned.Length >= 2)
            {
                var secondDigit = cleaned[1];
                if (secondDigit < '3' || secondDigit > '9')
                    return false;
            }

            return true;
        }
    }
}