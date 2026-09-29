using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Tests.SharedKernel;

public sealed class ErrorTests
{
    [Fact]
    public void DisplayCode_FormatsCodeWithAuxPrefix()
    {
        var error = Error.NotFound(14003, "Case not found");

        error.DisplayCode.ShouldBe("AUX-14003");
        error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void PreconditionFailed_HasItsOwnType()
    {
        Error.PreconditionFailed(10011, "Version mismatch").Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveCode_Throws(int code)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Error.Failure(code, "boom"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithBlankDescription_Throws(string description)
    {
        Should.Throw<ArgumentException>(() => Error.Conflict(14004, description));
    }

    [Fact]
    public void Validation_KeepsFieldErrors()
    {
        var error = Error.Validation(13001, "Invalid client", new Dictionary<string, string[]>
        {
            ["fiscalCode"] = ["validation.fiscalCode.invalid"],
        });

        error.ValidationErrors["fiscalCode"].ShouldBe(["validation.fiscalCode.invalid"]);
    }

    [Fact]
    public void NonValidationError_HasNoFieldErrors()
    {
        Error.Forbidden(29001, "Denied").ValidationErrors.ShouldBeEmpty();
    }

    [Fact]
    public void Errors_WithSameValues_AreEqual()
    {
        Error.Unauthorized(12001, "Invalid credentials").ShouldBe(Error.Unauthorized(12001, "Invalid credentials"));
    }
}
