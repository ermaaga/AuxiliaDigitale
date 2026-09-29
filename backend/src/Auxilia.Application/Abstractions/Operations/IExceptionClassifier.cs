using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Operations;

/// <summary>
/// Converts a known technical exception into its coded <see cref="Error"/> (e.g. the persistence layer maps
/// <c>DbUpdateConcurrencyException</c> to <c>AUX-10010</c>). Returns null for exceptions it does not know.
/// </summary>
public interface IExceptionClassifier
{
    Error? Classify(Exception exception);
}
