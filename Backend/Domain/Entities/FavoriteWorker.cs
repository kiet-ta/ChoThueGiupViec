using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table FAVORITE_WORKER. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class FavoriteWorker
{
    /// <summary>favorite_worker.customer_id INT (PK) (FK)</summary>
    public int CustomerId { get; set; }

    /// <summary>favorite_worker.worker_id INT (PK) (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>favorite_worker.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
