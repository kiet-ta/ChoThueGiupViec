using CommonService.Application.Common.Models;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Modules.Dispatch;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Disputes;
using CommonService.WebAPI.Controllers.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>ADMIN-UAT-05: the Admin incident monitor reads INCIDENT_LOG (contract dispatch.md 3.3); nothing is invented.</summary>
public class AdminIncidentTests
{
    [Theory]
    [InlineData("PENDING_REDISPATCH", "SEARCHING")]
    [InlineData("REPLACED", "REASSIGNED")]
    [InlineData("DECLINED_BY_CUSTOMER", "FAILED")]
    [InlineData("TIMEOUT_CANCELLED", "FAILED")]
    [InlineData("SOMETHING_NEW", "SOMETHING_NEW")]
    public void The_stored_status_is_mapped_to_the_three_values_of_the_contract(string stored, string expected) =>
        Assert.Equal(expected, AdminIncidentStatus.ToContract(stored));

    private sealed class Rows(params AdminIncidentDto[] items) : IAdminIncidentQuery
    {
        public int? LastLimit { get; private set; }

        public Task<IReadOnlyList<AdminIncidentDto>> ListAsync(int limit, CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<AdminIncidentDto>>(items);
        }
    }

    [Fact]
    public async Task The_controller_is_admin_only_and_answers_the_rows_in_the_envelope_with_the_limit_of_200()
    {
        var policy = typeof(AdminDispatchController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy;
        var row = new AdminIncidentDto(1, 2, 3, "ACCIDENT", "Flat tyre", null, 10.77m, 106.7m, DateTime.UtcNow, true, "SEARCHING", DateTime.UtcNow, null);
        var query = new Rows(row);

        var result = Assert.IsType<OkObjectResult>(await new AdminDispatchController(query).Incidents(default));

        Assert.Equal("AdminOnly", policy);
        var body = Assert.IsType<ApiResponse<IReadOnlyList<AdminIncidentDto>>>(result.Value);
        Assert.True(body.Success);
        Assert.Equal(row, Assert.Single(body.Data!));
        Assert.Equal(200, query.LastLimit);
    }

    [Fact]
    public async Task With_no_incident_the_answer_is_an_empty_list_not_invented_rows()
    {
        var result = Assert.IsType<OkObjectResult>(await new AdminDispatchController(new Rows()).Incidents(default));

        Assert.Empty(Assert.IsType<ApiResponse<IReadOnlyList<AdminIncidentDto>>>(result.Value).Data!);
    }

    [Fact]
    public async Task Real_database_an_incident_is_read_back_with_the_worker_of_its_assignment_the_mapped_status_and_the_deadline()
    {
        if (!DisputeEndpointTests.IsSqlServerAvailable()) return;

        var now = DateTime.UtcNow;
        var s = await DisputeEndpointTests.SeedAsync(now);
        long incidentId;
        try
        {
            await using (var db = new AppDbContext(DisputeEndpointTests.Options()))
            {
                var log = new IncidentLog
                {
                    AssignmentId = s.AssignmentIds[0],
                    IncidentType = "ACCIDENT",
                    Description = "Flat tyre on the way",
                    PhotoUrl = "/files/incident/x.png",
                    Latitude = 10.7769m,
                    Longitude = 106.7009m,
                    RedispatchStatus = "PENDING_REDISPATCH",
                    PenaltyWaived = true,
                    ReportedAt = now,
                };
                db.IncidentLogs.Add(log);
                await db.SaveChangesAsync();
                incidentId = log.IncidentId;
            }

            await using var read = new AppDbContext(DisputeEndpointTests.Options());
            var mine = (await new EfAdminIncidentQuery(read).ListAsync(200)).Single(i => i.IncidentId == incidentId);

            Assert.Equal(s.AssignmentIds[0], mine.AssignmentId);
            Assert.Equal(s.WorkerId, mine.WorkerId);
            Assert.Equal("ACCIDENT", mine.IncidentType);
            Assert.Equal("SEARCHING", mine.ReDispatchStatus);
            Assert.True(mine.IsPenaltyExempt);
            Assert.Equal("/files/incident/x.png", mine.PhotoEvidenceUrl);
            Assert.Equal(DateTimeKind.Utc, mine.ReportedAt.Kind);
            Assert.Equal(mine.ReportedAt.AddMinutes(IncidentService.RedispatchWindowMinutes), mine.ReDispatchDeadline);
            Assert.Null(mine.SubstituteWorkerId);
        }
        finally
        {
            await using (var db = new AppDbContext(DisputeEndpointTests.Options()))
            {
                await db.IncidentLogs.Where(i => s.AssignmentIds.Contains(i.AssignmentId)).ExecuteDeleteAsync();
            }

            await DisputeEndpointTests.CleanAsync(s);
        }
    }
}
