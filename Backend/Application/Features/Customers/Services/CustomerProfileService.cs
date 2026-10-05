using System.Net.Mail;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Implements GET/PUT /api/customers/me rules of contract customers.md §2.1 (BE-M1-04).
/// </summary>
public sealed class CustomerProfileService : ICustomerProfileService
{
    private const int MaxFullNameLength = 100;
    private const int MaxEmailLength = 255;

    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CustomerProfileService(ICustomerRepository customers, IUnitOfWork unitOfWork, IClock clock)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<CustomerProfileResult> GetAsync(int customerId, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(customerId, ct);
        return customer == null
            ? CustomerProfileResult.NotFound()
            : CustomerProfileResult.Ok(ToDto(customer));
    }

    public async Task<CustomerProfileResult> UpdateAsync(
        int customerId,
        UpdateCustomerProfileDto request,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length == 0)
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (fullName.Length > MaxFullNameLength)
        {
            errors["fullName"] = [$"Full name must be at most {MaxFullNameLength} characters."];
        }

        // A blank email means "no email" (a client form sends an empty field); anything else must be a valid address.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email != null && !IsValidEmail(email))
        {
            errors["email"] = [$"Email must be a valid address of at most {MaxEmailLength} characters."];
        }

        if (errors.Count > 0)
        {
            return CustomerProfileResult.ValidationError(errors);
        }

        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer == null)
        {
            return CustomerProfileResult.NotFound();
        }

        customer.FullName = fullName;
        customer.Email = email;
        customer.UpdatedAt = _clock.UtcNow;

        _customers.Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        return CustomerProfileResult.Ok(ToDto(customer));
    }

    private static CustomerProfileDto ToDto(Customer customer) => new()
    {
        CustomerId = customer.CustomerId,
        PhoneNumber = customer.PhoneNumber,
        FullName = customer.FullName,
        Email = customer.Email,
        TrustScore = customer.TrustScore,
        AccountStatus = DbEnum.ToDb(customer.AccountStatus),
        CreatedAt = customer.CreatedAt
    };

    private static bool IsValidEmail(string value) =>
        value.Length <= MaxEmailLength
        && MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value;
}
