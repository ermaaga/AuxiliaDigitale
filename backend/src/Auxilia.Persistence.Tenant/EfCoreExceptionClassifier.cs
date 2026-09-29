using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant;

/// <summary>Known EF Core exceptions → coded errors (ADR 0012): a stale <c>xmin</c> is a 409 <c>AUX-10010</c>.</summary>
internal sealed class EfCoreExceptionClassifier : IExceptionClassifier
{
    public Error? Classify(Exception exception) =>
        exception is DbUpdateConcurrencyException ? Errors.Host.ConcurrencyConflict() : null;
}
