using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Tests.SharedKernel;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.Conflict(14004, "Invalid transition");

    [Fact]
    public void Success_IsSuccessWithoutError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Failure_WithNullError_Throws()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void GenericFailure_ReadingValue_Throws()
    {
        Result<int> result = SampleError;

        var exception = Should.Throw<InvalidOperationException>(() => result.Value);
        exception.Message.ShouldContain("AUX-14004");
    }

    [Fact]
    public void Match_OnFailure_CallsFailureBranch()
    {
        Result<int> result = SampleError;

        result.Match(value => $"ok {value}", error => error.DisplayCode).ShouldBe("AUX-14004");
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        Result<int> result = 21;

        result.Map(value => value * 2).Value.ShouldBe(42);
    }

    [Fact]
    public void Map_OnFailure_KeepsError()
    {
        Result<int> result = SampleError;

        result.Map(value => value * 2).Error.ShouldBe(SampleError);
    }

    [Fact]
    public async Task BindAsync_OnSuccess_ChainsNextOperation()
    {
        Result<int> result = 1;

        var bound = await result.BindAsync(value => Task.FromResult<Result<string>>($"#{value}"));

        bound.Value.ShouldBe("#1");
    }

    [Fact]
    public async Task BindAsync_OnFailure_SkipsNextOperation()
    {
        Result<int> result = SampleError;
        var called = false;

        var bound = await result.BindAsync(value =>
        {
            called = true;
            return Task.FromResult<Result<string>>($"#{value}");
        });

        called.ShouldBeFalse();
        bound.Error.ShouldBe(SampleError);
    }
}
