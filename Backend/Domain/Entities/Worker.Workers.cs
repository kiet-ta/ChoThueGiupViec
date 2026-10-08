using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

public partial class Worker
{
    /// <summary>
    /// Helper method for profile updates by worker.
    /// </summary>
    public void UpdateProfile(string fullName)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            FullName = fullName.Trim();
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Update eKYC evaluation result.
    /// </summary>
    public void SetKycResult(decimal confidence, string status)
    {
        EkycConfidence = confidence;
        KycStatus = status;
        UpdatedAt = DateTime.UtcNow;
        if (status == "APPROVED" && WorkStatus == WorkStatus.Pending)
        {
            TransitionTo(WorkStatus.Idle);
        }
    }

    /// <summary>
    /// Admin manual review of worker eKYC.
    /// </summary>
    public void ReviewKyc(bool approved, int adminId)
    {
        KycReviewedBy = adminId;
        UpdatedAt = DateTime.UtcNow;

        if (approved)
        {
            KycStatus = "APPROVED";
            if (WorkStatus == WorkStatus.Pending || WorkStatus == WorkStatus.Locked)
            {
                TransitionTo(WorkStatus.Idle);
            }
        }
        else
        {
            KycStatus = "REJECTED";
            if (WorkStatus == WorkStatus.Pending || WorkStatus == WorkStatus.Idle || WorkStatus == WorkStatus.Busy)
            {
                TransitionTo(WorkStatus.Locked);
            }
        }
    }
}

