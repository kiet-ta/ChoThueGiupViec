using System.Collections.Generic;

namespace Backend.Application.Features.Workers.Commands
{
    public class ImportWorkerResult
    {
        public int SuccessCount { get; set; }
        public List<ImportWorkerError> Errors { get; set; } = new();
    }

    public class ImportWorkerError
    {
        public int LineNumber { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string RawData { get; set; } = string.Empty;
    }
}