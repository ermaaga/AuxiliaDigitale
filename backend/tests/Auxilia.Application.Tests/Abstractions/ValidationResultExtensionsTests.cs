using Auxilia.Application.Abstractions.Validation;
using Auxilia.Diagnostics;

using FluentValidation;

namespace Auxilia.Application.Tests.Abstractions;

public sealed class ValidationResultExtensionsTests
{
    [Fact]
    public void ToError_MapsFieldsToCamelCaseAndCodesToTranslationKeys()
    {
        var validation = new SampleValidator().Validate(new Sample(string.Empty, "RSS", new Address(string.Empty)));

        var error = validation.ToError();

        error.Code.ShouldBe(EventCodes.Host.ValidationFailed);
        error.ValidationErrors["lastName"].ShouldBe(["validation.notEmpty"]);
        error.ValidationErrors["fiscalCode"].ShouldBe(["validation.fiscalCode.invalid"]);
        error.ValidationErrors["address.city"].ShouldBe(["validation.notEmpty"]);
    }

    [Fact]
    public void ToResult_IsFailureWithValidationError()
    {
        var validation = new SampleValidator().Validate(new Sample(string.Empty, "RSSMRA85T10A562S", new Address("Roma")));

        var result = validation.ToResult<int>();

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(SharedKernel.Results.ErrorType.Validation);
    }

    private sealed record Sample(string LastName, string FiscalCode, Address Address);

    private sealed record Address(string City);

    private sealed class SampleValidator : AbstractValidator<Sample>
    {
        public SampleValidator()
        {
            RuleFor(sample => sample.LastName).NotEmpty();
            RuleFor(sample => sample.FiscalCode).Length(16).WithErrorCode("validation.fiscalCode.invalid");
            RuleFor(sample => sample.Address.City).NotEmpty();
        }
    }
}
