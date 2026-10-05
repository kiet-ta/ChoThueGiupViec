using CommonService.Application.Common.Options;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Implements the address book of contract customers.md §2.2 (BE-M1-05): server-side S_total (PRD §1.1),
/// validation by housing type (decisions Q21 C4), single default address, ownership and order-reference rules (C5).
/// </summary>
public sealed class CustomerAddressService : ICustomerAddressService
{
    private const int MaxLabelLength = 50;
    private const int MaxAddressLineLength = 255;
    private const int MaxDistrictOrCityLength = 100;
    private const decimal MaxFloorAreaM2 = 9999.99m; // DECIMAL(6,2)
    private const int MaxTinyInt = 255;              // TINYINT

    private readonly ICustomerRepository _customers;
    private readonly ICustomerAddressRepository _addresses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly BusinessRules _rules;

    public CustomerAddressService(
        ICustomerRepository customers,
        ICustomerAddressRepository addresses,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<BusinessRules> rules)
    {
        _customers = customers;
        _addresses = addresses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _rules = rules.Value;
    }

    public async Task<CustomerAddressResult<IReadOnlyList<AddressDto>>> ListAsync(int customerId, CancellationToken ct = default)
    {
        var list = await _addresses.ListByCustomerAsync(customerId, ct);
        return CustomerAddressResult<IReadOnlyList<AddressDto>>.Ok(list.Select(ToDto).ToList());
    }

    public async Task<CustomerAddressResult<AddressDto>> GetAsync(int customerId, int addressId, CancellationToken ct = default)
    {
        var address = await _addresses.GetOwnedAsync(customerId, addressId, ct);
        return address == null
            ? CustomerAddressResult<AddressDto>.NotFound()
            : CustomerAddressResult<AddressDto>.Ok(ToDto(address));
    }

    public async Task<CustomerAddressResult<AddressDto>> CreateAsync(int customerId, AddressRequestDto request, CancellationToken ct = default)
    {
        var (valid, errors) = Validate(request);
        if (valid == null)
        {
            return CustomerAddressResult<AddressDto>.ValidationError(errors);
        }

        if (await _customers.GetByIdAsync(customerId, ct) == null)
        {
            return CustomerAddressResult<AddressDto>.NotFound("Customer not found.");
        }

        var created = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var existing = await _addresses.ListByCustomerAsync(customerId, ct);

            // The first address is always the default; later ones only when the client asks for it.
            var makeDefault = existing.Count == 0 || valid.IsDefault;
            if (makeDefault)
            {
                ClearDefault(existing);
            }

            var address = new CustomerAddress
            {
                CustomerId = customerId,
                CreatedAt = _clock.UtcNow
            };
            Apply(address, valid);
            address.IsDefault = makeDefault;

            await _addresses.AddAsync(address, ct);
            return address;
        }, ct);

        return CustomerAddressResult<AddressDto>.Created(ToDto(created));
    }

    public async Task<CustomerAddressResult<AddressDto>> UpdateAsync(
        int customerId,
        int addressId,
        AddressRequestDto request,
        CancellationToken ct = default)
    {
        var (valid, errors) = Validate(request);
        if (valid == null)
        {
            return CustomerAddressResult<AddressDto>.ValidationError(errors);
        }

        var updated = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var address = await _addresses.GetOwnedAsync(customerId, addressId, ct);
            if (address == null)
            {
                return null;
            }

            Apply(address, valid);

            // isDefault = true moves the default to this address; false never removes an existing default.
            if (valid.IsDefault && !address.IsDefault)
            {
                var all = await _addresses.ListByCustomerAsync(customerId, ct);
                ClearDefault(all);
                address.IsDefault = true;
            }

            return address;
        }, ct);

        return updated == null
            ? CustomerAddressResult<AddressDto>.NotFound()
            : CustomerAddressResult<AddressDto>.Ok(ToDto(updated));
    }

    public async Task<CustomerAddressResult<object?>> DeleteAsync(int customerId, int addressId, CancellationToken ct = default)
    {
        var outcome = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var address = await _addresses.GetOwnedAsync(customerId, addressId, ct);
            if (address == null)
            {
                return DeleteOutcome.NotFound;
            }

            if (await _addresses.IsReferencedByOrderAsync(addressId, ct))
            {
                return DeleteOutcome.Referenced;
            }

            var wasDefault = address.IsDefault;
            _addresses.Delete(address);

            if (wasDefault)
            {
                // The most recently created remaining address becomes the default (none left: no default).
                var next = (await _addresses.ListByCustomerAsync(customerId, ct))
                    .Where(a => a.AddressId != addressId)
                    .OrderByDescending(a => a.CreatedAt)
                    .ThenByDescending(a => a.AddressId)
                    .FirstOrDefault();
                if (next != null)
                {
                    next.IsDefault = true;
                }
            }

            return DeleteOutcome.Deleted;
        }, ct);

        return outcome switch
        {
            DeleteOutcome.NotFound => CustomerAddressResult<object?>.NotFound(),
            DeleteOutcome.Referenced => CustomerAddressResult<object?>.Conflict(
                "The address is used by an order and cannot be deleted."),
            _ => CustomerAddressResult<object?>.Ok(null)
        };
    }

    private enum DeleteOutcome
    {
        Deleted,
        NotFound,
        Referenced
    }

    private sealed record ValidAddress(
        string Label,
        string AddressLine,
        string District,
        string City,
        HousingType HousingType,
        decimal FloorAreaM2,
        byte NumFloors,
        byte? Bedrooms,
        byte? Bathrooms,
        decimal Latitude,
        decimal Longitude,
        bool IsDefault);

    private (ValidAddress? Valid, Dictionary<string, string[]> Errors) Validate(AddressRequestDto? request)
    {
        var errors = new Dictionary<string, string[]>();
        request ??= new AddressRequestDto();

        var label = RequiredText(request.Label, "label", MaxLabelLength, errors);
        var addressLine = RequiredText(request.AddressLine, "addressLine", MaxAddressLineLength, errors);
        var district = RequiredText(request.District, "district", MaxDistrictOrCityLength, errors);
        var city = RequiredText(request.City, "city", MaxDistrictOrCityLength, errors);

        HousingType? housingType = null;
        var housingCode = (request.HousingType ?? string.Empty).Trim().ToUpperInvariant();
        foreach (var candidate in Enum.GetValues<HousingType>())
        {
            if (DbEnum.ToDb(candidate) == housingCode)
            {
                housingType = candidate;
            }
        }

        if (housingType == null)
        {
            errors["housingType"] = ["Housing type must be one of APARTMENT, HOUSE, ROOM."];
        }

        // DECIMAL(6,2): round first so the stored value, the S_total and the rules all use the same number.
        decimal? floorArea = request.FloorAreaM2 is { } rawArea
            ? Math.Round(rawArea, 2, MidpointRounding.AwayFromZero)
            : null;
        if (floorArea == null || floorArea <= 0 || floorArea > MaxFloorAreaM2)
        {
            errors["floorAreaM2"] = [$"Floor area must be greater than 0 and at most {MaxFloorAreaM2} m2."];
        }
        else if (housingType == HousingType.Room && floorArea > _rules.Address.RoomMaxAreaM2)
        {
            errors["floorAreaM2"] = [$"A room must have a floor area of at most {_rules.Address.RoomMaxAreaM2} m2."];
        }

        if (request.NumFloors is not { } floors || floors < 1 || floors > MaxTinyInt)
        {
            errors["numFloors"] = [$"Number of floors must be an integer from 1 to {MaxTinyInt}."];
        }
        else if (housingType is HousingType.Apartment or HousingType.Room && floors != 1)
        {
            errors["numFloors"] = ["An apartment or a room has exactly 1 floor."];
        }
        else if (housingType == HousingType.House && floors > _rules.Address.HouseMaxFloors)
        {
            errors["numFloors"] = [$"A house has between 1 and {_rules.Address.HouseMaxFloors} floors."];
        }

        var bedrooms = OptionalCount(request.Bedrooms, "bedrooms", errors);
        var bathrooms = OptionalCount(request.Bathrooms, "bathrooms", errors);

        // DECIMAL(9,6)
        decimal? latitude = request.Latitude is { } rawLat
            ? Math.Round(rawLat, 6, MidpointRounding.AwayFromZero)
            : null;
        if (latitude == null || latitude < -90m || latitude > 90m)
        {
            errors["latitude"] = ["Latitude must be between -90 and 90."];
        }

        decimal? longitude = request.Longitude is { } rawLon
            ? Math.Round(rawLon, 6, MidpointRounding.AwayFromZero)
            : null;
        if (longitude == null || longitude < -180m || longitude > 180m)
        {
            errors["longitude"] = ["Longitude must be between -180 and 180."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ValidAddress(
            label!,
            addressLine!,
            district!,
            city!,
            housingType!.Value,
            floorArea!.Value,
            (byte)request.NumFloors!.Value,
            bedrooms,
            bathrooms,
            latitude!.Value,
            longitude!.Value,
            request.IsDefault), errors);
    }

    private static string? RequiredText(string? value, string field, int maxLength, Dictionary<string, string[]> errors)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > maxLength)
        {
            errors[field] = [$"{field} is required and must be at most {maxLength} characters."];
            return null;
        }

        return text;
    }

    private static byte? OptionalCount(int? value, string field, Dictionary<string, string[]> errors)
    {
        if (value == null)
        {
            return null;
        }

        if (value < 0 || value > MaxTinyInt)
        {
            errors[field] = [$"{field} must be an integer from 0 to {MaxTinyInt}."];
            return null;
        }

        return (byte)value.Value;
    }

    private static void Apply(CustomerAddress address, ValidAddress valid)
    {
        address.Label = valid.Label;
        address.AddressLine = valid.AddressLine;
        address.District = valid.District;
        address.City = valid.City;
        address.HousingType = valid.HousingType;
        address.FloorAreaM2 = valid.FloorAreaM2;
        address.NumFloors = valid.NumFloors;
        address.Bedrooms = valid.Bedrooms;
        address.Bathrooms = valid.Bathrooms;
        address.Latitude = valid.Latitude;
        address.Longitude = valid.Longitude;
    }

    private static void ClearDefault(IEnumerable<CustomerAddress> addresses)
    {
        foreach (var address in addresses.Where(a => a.IsDefault))
        {
            address.IsDefault = false;
        }
    }

    /// <summary>S_total = S_floor x N_floors (PRD §1.1), 2 decimals, round half away from zero (decisions G-2).</summary>
    public static decimal ComputeTotalArea(decimal floorAreaM2, int numFloors) =>
        Math.Round(floorAreaM2 * numFloors, 2, MidpointRounding.AwayFromZero);

    private static AddressDto ToDto(CustomerAddress address) => new()
    {
        AddressId = address.AddressId,
        Label = address.Label,
        AddressLine = address.AddressLine,
        District = address.District,
        City = address.City,
        HousingType = DbEnum.ToDb(address.HousingType),
        FloorAreaM2 = address.FloorAreaM2,
        NumFloors = address.NumFloors,
        TotalAreaM2 = ComputeTotalArea(address.FloorAreaM2, address.NumFloors),
        Bedrooms = address.Bedrooms,
        Bathrooms = address.Bathrooms,
        Latitude = address.Latitude,
        Longitude = address.Longitude,
        IsDefault = address.IsDefault,
        CreatedAt = address.CreatedAt
    };
}
