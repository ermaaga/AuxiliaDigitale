using System.Reflection;

using Auxilia.Diagnostics;

namespace Auxilia.Architecture.Tests;

/// <summary>
/// The <c>Operations</c> catalog (ADR 0004, skill auxilia-log-codes): unique names <c>&lt;Range&gt;.&lt;Field&gt;</c>, success
/// codes declared in the same <c>EventCodes</c> range and owned by one operation only.
/// </summary>
public sealed class OperationCatalogTests
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void OperationClasses_MatchAnEventCodesRange()
    {
        var ranges = EventRegistry.Ranges().Select(range => range.Name).ToHashSet();

        typeof(Operations).GetNestedTypes().Select(type => type.Name).Where(name => !ranges.Contains(name)).ShouldBeEmpty();
    }

    [Fact]
    public void Operations_AreReadOnlyFieldsNamedAfterRangeAndField()
    {
        var violations = new List<string>();

        foreach (var type in typeof(Operations).GetNestedTypes())
        {
            foreach (var member in type.GetMembers(PublicStatic).Where(member => member is FieldInfo or PropertyInfo))
            {
                if (member is not FieldInfo { IsInitOnly: true } field || field.FieldType != typeof(OperationDescriptor))
                {
                    violations.Add($"Operations.{type.Name}.{member.Name} must be a static readonly OperationDescriptor field");
                    continue;
                }

                var operation = (OperationDescriptor)field.GetValue(null)!;
                if (operation.Name != $"{type.Name}.{field.Name}")
                {
                    violations.Add($"Operations.{type.Name}.{field.Name} must be named '{type.Name}.{field.Name}', not '{operation.Name}'");
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Operations_HaveUniqueNamesAndSuccessCodes()
    {
        var operations = EventRegistry.OperationCatalog();

        operations.Select(operation => operation.Name).ShouldBeUnique();
        operations.Select(operation => operation.SuccessCode).ShouldBeUnique();
    }

    [Fact]
    public void Operations_UseASuccessCodeOfTheirRange()
    {
        var violations = new List<string>();

        foreach (var type in typeof(Operations).GetNestedTypes())
        {
            var codes = typeof(EventCodes).GetNestedType(type.Name)?.GetFields(PublicStatic)
                .Where(field => field.IsLiteral)
                .Select(field => (int)field.GetRawConstantValue()!)
                .ToHashSet() ?? [];

            foreach (var field in type.GetFields(PublicStatic).Where(field => field.FieldType == typeof(OperationDescriptor)))
            {
                var operation = (OperationDescriptor)field.GetValue(null)!;
                if (!codes.Contains(operation.SuccessCode))
                {
                    violations.Add($"{operation.Name} uses {operation.SuccessCode}, not a code of EventCodes.{type.Name}");
                }
            }
        }

        violations.ShouldBeEmpty();
    }
}
