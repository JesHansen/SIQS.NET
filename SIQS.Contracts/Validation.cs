using System.Collections.Immutable;

namespace SIQS.Contracts;

/// <summary>A single structural validation problem with a stable machine-readable code.</summary>
public sealed record ValidationIssue(string Code, string Message);

/// <summary>Aggregate result of a set of structural validation checks.</summary>
public sealed record ValidationResult : IEquatable<ValidationResult>
{
    private static readonly ValidationResult Valid = new(true, Array.Empty<ValidationIssue>());

    public ValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues)
    {
        this.IsValid = IsValid;
        this.Issues = Array.AsReadOnly(Issues.ToArray());
    }

    public bool IsValid { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>A successful, issue-free result.</summary>
    public static ValidationResult Ok() => Valid;

    /// <summary>Combines several results; the merged result is valid only if all inputs are.</summary>
    public static ValidationResult Merge(params ValidationResult[] results)
    {
        var issues = results.SelectMany(r => r.Issues).ToImmutableArray();
        return issues.IsEmpty ? Valid : new ValidationResult(false, issues);
    }
}

/// <summary>Accumulates validation issues and produces an immutable <see cref="ValidationResult"/>.</summary>
public sealed class ValidationResultBuilder
{
    private readonly List<ValidationIssue> _issues = new();

    /// <summary>Records a validation error.</summary>
    public ValidationResultBuilder Error(string code, string message)
    {
        _issues.Add(new ValidationIssue(code, message));
        return this;
    }

    /// <summary>Records an error only when <paramref name="condition"/> is true.</summary>
    public ValidationResultBuilder ErrorIf(bool condition, string code, string message)
        => condition ? Error(code, message) : this;

    /// <summary>True when no errors have been recorded yet.</summary>
    public bool IsValid => _issues.Count == 0;

    /// <summary>Builds the immutable result.</summary>
    public ValidationResult Build()
        => _issues.Count == 0
            ? ValidationResult.Ok()
            : new ValidationResult(false, _issues.ToImmutableArray());
}
