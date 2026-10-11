using CommonService.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CommonService.Infrastructure.Modules.Agencies
{
    public interface IWorkerImportService
    {
        /// <summary>
        /// Imports workers from a CSV stream.
        /// </summary>
        /// <param name="csvStream">The CSV stream containing worker data.</param>
        /// <param name="agencyId">The ID of the agency importing the workers.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A result indicating success or failure, with any errors.</returns>
        Task<WorkerImportResult> ImportWorkersAsync(Stream csvStream, int agencyId, CancellationToken cancellationToken = default);
    }

    public class WorkerImportResult
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new();
        public int WorkersCreated { get; set; }
    }
}