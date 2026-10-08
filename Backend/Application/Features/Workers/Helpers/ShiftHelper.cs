using CommonService.Application.Exceptions;

namespace CommonService.Application.Features.Workers.Helpers;

public static class ShiftHelper
{
    public const string ShiftMorning = "SHIFT_MORNING";
    public const string ShiftAfternoon = "SHIFT_AFTERNOON";
    public const string ShiftEvening = "SHIFT_EVENING";

    public static string NormalizeShiftCode(string shiftCode)
    {
        if (string.IsNullOrWhiteSpace(shiftCode))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "shiftCode", ["shiftCode is required."] }
            });
        }

        var normalized = shiftCode.Trim().ToUpperInvariant();
        return normalized switch
        {
            "SHIFT_MORNING" or "MORNING" => ShiftMorning,
            "SHIFT_AFTERNOON" or "AFTERNOON" => ShiftAfternoon,
            "SHIFT_EVENING" or "EVENING" => ShiftEvening,
            _ => throw new ValidationException(new Dictionary<string, string[]>
            {
                { "shiftCode", [$"Invalid shiftCode '{shiftCode}'. Valid values are SHIFT_MORNING, SHIFT_AFTERNOON, SHIFT_EVENING."] }
            })
        };
    }

    public static (TimeOnly StartTime, TimeOnly EndTime) GetShiftTimes(string shiftCode)
    {
        var normalized = NormalizeShiftCode(shiftCode);
        return normalized switch
        {
            ShiftMorning => (new TimeOnly(8, 0), new TimeOnly(12, 0)),
            ShiftAfternoon => (new TimeOnly(13, 0), new TimeOnly(17, 0)),
            ShiftEvening => (new TimeOnly(17, 30), new TimeOnly(20, 30)),
            _ => throw new InvalidOperationException()
        };
    }
}
