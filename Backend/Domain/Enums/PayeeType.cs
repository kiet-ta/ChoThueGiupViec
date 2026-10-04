namespace CommonService.Domain.Enums;

/// <summary>Payout item payee (drawio PAYOUT_ITEM.payee_type): FREELANCER requires worker_id, AGENCY requires agency_id.</summary>
public enum PayeeType
{
    Freelancer,
    Agency
}
