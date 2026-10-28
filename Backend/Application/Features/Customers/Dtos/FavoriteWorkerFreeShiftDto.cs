namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>One shift a favorite worker is free for (contract customers.md §2.3): the booking form greys out every other shift.</summary>
public sealed class FavoriteWorkerFreeShiftDto
{
    public DateOnly Date { get; set; }

    /// <summary>SHIFT_MORNING, SHIFT_AFTERNOON or SHIFT_EVENING.</summary>
    public string ShiftCode { get; set; } = string.Empty;
}
