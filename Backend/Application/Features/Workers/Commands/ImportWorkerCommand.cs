using System;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Backend.Application.Features.Workers.Commands
{
    public class ImportWorkerCommand : IRequest<ImportWorkerResult>
    {
        public int AgencyId { get; set; }
        public IFormFile File { get; set; } = null!;
        public bool HasHeader { get; set; } = true;
    }
}