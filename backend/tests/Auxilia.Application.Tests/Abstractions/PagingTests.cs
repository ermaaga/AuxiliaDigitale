using Auxilia.Application.Abstractions.Paging;

namespace Auxilia.Application.Tests.Abstractions;

public sealed class PagingTests
{
    private static readonly Row[] Rows =
    [
        new(Guid.Parse("00000000-0000-7000-8000-000000000003"), "Rossi", 30),
        new(Guid.Parse("00000000-0000-7000-8000-000000000001"), "Bianchi", 40),
        new(Guid.Parse("00000000-0000-7000-8000-000000000002"), "Rossi", 20),
    ];

    private readonly SortMap<Row> sorts = new SortMap<Row>("lastName", row => row.Id)
        .Add("lastName", row => row.LastName)
        .Add("age", row => row.Age);

    [Fact]
    public void Apply_NoSort_UsesDefaultThenTiebreaker()
    {
        var sorted = sorts.Apply(Rows.AsQueryable(), null).Value.ToArray();

        sorted.Select(row => row.Age).ShouldBe([40, 20, 30]);
    }

    [Fact]
    public void Apply_DescendingAndSecondaryField_SortsInOrder()
    {
        var sorted = sorts.Apply(Rows.AsQueryable(), "-lastName, -age").Value.ToArray();

        sorted.Select(row => row.Age).ShouldBe([30, 20, 40]);
    }

    [Fact]
    public void Apply_UnknownField_IsValidationError()
    {
        var result = sorts.Apply(Rows.AsQueryable(), "password");

        result.IsFailure.ShouldBeTrue();
        result.Error!.ValidationErrors["sort"].ShouldBe([SortMap<Row>.UnknownFieldKey]);
    }

    [Theory]
    [InlineData(0, 25, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, PageRequest.MaxPageSize + 1, false)]
    [InlineData(1, PageRequest.MaxPageSize, true)]
    [InlineData(3, 10, true)]
    public void PageRequestValidator_EnforcesBounds(int page, int pageSize, bool valid)
    {
        new PageRequestValidator().Validate(new PageRequest { Page = page, PageSize = pageSize }).IsValid.ShouldBe(valid);
    }

    [Fact]
    public void Skip_IsOffsetOfThePage()
    {
        new PageRequest { Page = 3, PageSize = 10 }.Skip.ShouldBe(20);
    }

    private sealed record Row(Guid Id, string LastName, int Age);
}
