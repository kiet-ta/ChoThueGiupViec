using System.Reflection;
using System.Runtime.CompilerServices;
using CommonService.Infrastructure.Modules.Agencies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Agencies;

/// <summary>
/// BE-M5-01: a concurrent duplicate registration must become a 409, so the repository has to recognize a SQL Server
/// unique-key violation. No SQL Server is needed: the SqlException is built by reflection.
/// </summary>
public class SqlUniqueViolationTests
{
    [Theory]
    [InlineData(2601, true)] // duplicate key in a unique index
    [InlineData(2627, true)] // unique / primary key constraint
    [InlineData(547, false)] // foreign key
    [InlineData(1205, false)] // deadlock
    [InlineData(0, false)]
    public void Only_unique_violation_numbers_are_recognized(int number, bool expected)
    {
        Assert.Equal(expected, SqlUniqueViolation.IsUniqueViolationNumber(number));
    }

    [Fact]
    public void Wrapped_unique_violation_is_found_inside_a_DbUpdateException()
    {
        var sql = CreateSqlException(2601);

        var wrapped = new DbUpdateException("save failed", sql);

        Assert.True(SqlUniqueViolation.IsUniqueViolation(wrapped));
    }

    [Fact]
    public void Foreign_key_violation_and_plain_exceptions_are_not_unique_violations()
    {
        Assert.False(SqlUniqueViolation.IsUniqueViolation(new DbUpdateException("fk", CreateSqlException(547))));
        Assert.False(SqlUniqueViolation.IsUniqueViolation(new DbUpdateException("other", new InvalidOperationException())));
        Assert.False(SqlUniqueViolation.IsUniqueViolation(new DbUpdateException("no inner")));
    }

    private static SqlException CreateSqlException(int number)
    {
        var errorType = typeof(SqlError);
        var error = (SqlError)RuntimeHelpers.GetUninitializedObject(errorType);
        SetField(error, "_number", number);
        SetField(error, "_message", "test");

        var collection = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        var listField = typeof(SqlErrorCollection)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => typeof(System.Collections.IList).IsAssignableFrom(f.FieldType));
        var list = (System.Collections.IList)Activator.CreateInstance(listField.FieldType)!;
        list.Add(error);
        listField.SetValue(collection, list);

        var exception = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        var errorsField = typeof(SqlException)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(SqlErrorCollection));
        errorsField.SetValue(exception, collection);
        return exception;
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"Field {name} not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
