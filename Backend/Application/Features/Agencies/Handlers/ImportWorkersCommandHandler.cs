using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Workers;
using CommonService.Application.Common.Models;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handles importing workers from CSV file
/// </summary>
public sealed class ImportWorkersCommandHandler(
    IAgencyRepository agencyRepository,
    IWorkerRepository workerRepository,
    IConfiguration configuration)
    : IRequestHandler<ImportWorkersCommand, ApiResponse<ImportWorkerResponseDto>>
{
    public async Task<ApiResponse<ImportWorkerResponseDto>> Handle(ImportWorkersCommand command, CancellationToken cancellationToken)
    {
        ImportWorkerResponseDto response;

        if (string.IsNullOrWhiteSpace(command.CsvContent))
        {
            response = new ImportWorkerResponseDto
            {
                SuccessfullyImported = 0,
                Errors = new List<ImportWorkerErrorDto>()
            };
            return ApiResponse<ImportWorkerResponseDto>.Fail("CSV content is required.", response);
        }

        // Validate agency exists and has guarantee signed (Q19)
        var agency = await agencyRepository.GetByIdAsync(command.AgencyId, cancellationToken);
        if (agency == null)
        {
            response = new ImportWorkerResponseDto
            {
                SuccessfullyImported = 0,
                Errors = new List<ImportWorkerErrorDto>()
            };
            return ApiResponse<ImportWorkerResponseDto>.Fail("Agency not found.", response);
        }

        if (!agency.GuaranteeSignedAt.HasValue)
        {
            response = new ImportWorkerResponseDto
            {
                SuccessfullyImported = 0,
                Errors = new List<ImportWorkerErrorDto>()
            };
            return ApiResponse<ImportWorkerResponseDto>.Fail("Guarantee must be signed before importing workers.", response);
        }

        // Parse CSV content
        var lines = command.CsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            response = new ImportWorkerResponseDto
            {
                SuccessfullyImported = 0,
                Errors = new List<ImportWorkerErrorDto>()
            };
            return ApiResponse<ImportWorkerResponseDto>.Fail("CSV file is empty.", response);
        }

        // Skip header line if it exists
        var startIndex = 0;
        if (IsHeaderLine(lines[0]))
        {
            startIndex = 1;
        }

        int successfullyImported = 0;
        var errors = new List<ImportWorkerErrorDto>();

        // Process each line
        for (int i = startIndex; i < lines.Length; i++)
        {
            var lineNumber = i + 1; // 1-based for reporting
            var line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue; // Skip empty lines
            }

            try
            {
                var workerDto = ParseCsvLine(line, lineNumber);
                if (workerDto == null)
                {
                    errors.Add(new ImportWorkerErrorDto
                    {
                        LineNumber = lineNumber,
                        ErrorMessage = "Invalid CSV format",
                        InvalidValue = line.Length > 100 ? line.Substring(0, 100) + "..." : line
                    });
                    continue;
                }

                // Validate the worker data
                ValidateWorkerDto(workerDto, lineNumber, errors);

                // If not dry run, create the worker
                if (!command.DryRun)
                {
                    await CreateWorkerAsync(workerDto, command.AgencyId, cancellationToken, workerRepository);
                }

                successfullyImported++;
            }
            catch (Exception ex)
            {
                errors.Add(new ImportWorkerErrorDto
                {
                    LineNumber = lineNumber,
                    ErrorMessage = ex.Message,
                    InvalidValue = line.Length > 100 ? line.Substring(0, 100) + "..." : line
                });
            }
        }

        // If not dry run and we successfully processed all lines (no errors), save changes
        if (!command.DryRun && errors.Count == 0)
        {
            await agencyRepository.SaveChangesAsync(cancellationToken);
            await workerRepository.SaveChangesAsync(cancellationToken);
        }

        // Create the response
        response = new ImportWorkerResponseDto
        {
            SuccessfullyImported = successfullyImported,
            Errors = errors
        };

        // Determine response status
        if (errors.Count > 0)
        {
            if (successfullyImported == 0)
            {
                // All lines failed
                return ApiResponse<ImportWorkerResponseDto>.Fail("All records failed validation.", response);
            }
            else
            {
                // Partial success
                return ApiResponse<ImportWorkerResponseDto>.Ok(response,
                    $"{successfullyImported} record(s) imported successfully, {errors.Count} record(s) failed.");
            }
        }
        else
        {
            // All successful
            return ApiResponse<ImportWorkerResponseDto>.Ok(response,
                $"{successfullyImported} record(s) imported successfully.");
        }
    }

    private bool IsHeaderLine(string line)
    {
        var lowerLine = line.ToLowerInvariant();
        return lowerLine.Contains("phone") && lowerLine.Contains("national") && lowerLine.Contains("fullname");
    }

    private ImportWorkerRequestDto? ParseCsvLine(string line, int lineNumber)
    {
        try
        {
            // Simple CSV parsing - assuming no quoted commas for simplicity
            // In production, you might want to use a proper CSV parser like CsvHelper
            var parts = line.Split(',');
            if (parts.Length < 3)
            {
                return null;
            }

            return new ImportWorkerRequestDto
            {
                PhoneNumber = parts[0].Trim(),
                NationalId = parts[1].Trim(),
                FullName = parts[2].Trim()
            };
        }
        catch
        {
            return null;
        }
    }

    private void ValidateWorkerDto(ImportWorkerRequestDto dto, int lineNumber, List<ImportWorkerErrorDto> errors)
    {
        // Validate phone number
        if (string.IsNullOrWhiteSpace(dto.PhoneNumber))
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "PhoneNumber",
                ErrorMessage = "Phone number is required",
                InvalidValue = dto.PhoneNumber
            });
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(dto.PhoneNumber, @"^\+?[\d\s\-\(\)]{10,15}$"))
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "PhoneNumber",
                ErrorMessage = "Invalid phone number format",
                InvalidValue = dto.PhoneNumber
            });
            return;
        }

        // Validate national ID
        if (string.IsNullOrWhiteSpace(dto.NationalId))
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "NationalId",
                ErrorMessage = "National ID is required",
                InvalidValue = dto.NationalId
            });
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(dto.NationalId, @"^\d{12}$"))
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "NationalId",
                ErrorMessage = "National ID must be exactly 12 digits",
                InvalidValue = dto.NationalId
            });
            return;
        }

        // Validate full name
        if (string.IsNullOrWhiteSpace(dto.FullName))
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "FullName",
                ErrorMessage = "Full name is required",
                InvalidValue = dto.FullName
            });
            return;
        }

        if (dto.FullName.Length > 100)
        {
            errors.Add(new ImportWorkerErrorDto
            {
                LineNumber = lineNumber,
                FieldName = "FullName",
                ErrorMessage = "Full name cannot exceed 100 characters",
                InvalidValue = dto.FullName
            });
            return;
        }

        // Check for duplicates within the batch (simplified - in production you'd want to check against database too)
        // For now, we'll rely on database constraints to catch duplicates
    }

    private async Task CreateWorkerAsync(ImportWorkerRequestDto dto, int agencyId, CancellationToken cancellationToken, IWorkerRepository workerRepository)
    {
        // Check if worker already exists with this phone number or national ID
        var existsByPhone = await workerRepository.GetByPhoneNumberAsync(dto.PhoneNumber, cancellationToken) != null;
        var existsByNationalId = await workerRepository.GetByNationalIdAsync(dto.NationalId, cancellationToken) != null;

        if (existsByPhone || existsByNationalId)
        {
            throw new InvalidOperationException($"A worker with this phone number or national ID already exists.");
        }

        // Create worker entity as agency staff
        var worker = Worker.CreateAgencyStaff(
            agencyId,
            dto.PhoneNumber.Normalize(),
            dto.NationalId.Normalize(),
            dto.FullName.Normalize()
        );

        // Set additional values
        worker.RatingAvg = 5.00m;
        worker.CompletedJobs = 0;
        worker.CreatedAt = DateTime.UtcNow;
        worker.UpdatedAt = DateTime.UtcNow;

        // Add worker to repository
        await workerRepository.AddAsync(worker, cancellationToken);
    }
}