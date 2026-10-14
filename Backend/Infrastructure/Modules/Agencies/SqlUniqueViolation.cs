using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>Recognizes a SQL Server unique-key violation inside an EF Core <see cref="DbUpdateException"/>.</summary>
public static class SqlUniqueViolation
{
    // SQL Server error numbers: 2601 = duplicate key row in a unique index, 2627 = violation of a UNIQUE/PRIMARY KEY
    // constraint (https://learn.microsoft.com/sql/relational-databases/errors-events/database-engine-events-and-errors).
    public const int DuplicateKeyInUniqueIndex = 2601;
    public const int UniqueConstraintViolation = 2627;

    public static bool IsUniqueViolation(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && IsUniqueViolationNumber(sql.Number))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUniqueViolationNumber(int sqlErrorNumber) =>
        sqlErrorNumber is DuplicateKeyInUniqueIndex or UniqueConstraintViolation;
}
