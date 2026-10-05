namespace CommonService.Domain.Enums;

/// <summary>Single Table Inheritance discriminator of Worker (PRD 2.2): worker_type FREELANCER | AGENCY_STAFF.</summary>
public enum WorkerType
{
    Freelancer,
    AgencyStaff
}
