using CommonService.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CommonService.Infrastructure.Persistence.Converters;

/// <summary>
/// Converts enum members to/from UPPER_SNAKE_CASE strings using DbEnum.
/// </summary>
public class DbEnumValueConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public DbEnumValueConverter() : base(
        v => DbEnum.ToDb(v),
        v => DbEnum.Parse<TEnum>(v))
    {
    }
}

public class NullableDbEnumValueConverter<TEnum> : ValueConverter<TEnum?, string?>
    where TEnum : struct, Enum
{
    public NullableDbEnumValueConverter() : base(
        v => v.HasValue ? DbEnum.ToDb(v.Value) : null,
        v => !string.IsNullOrEmpty(v) ? DbEnum.Parse<TEnum>(v) : null)
    {
    }
}
