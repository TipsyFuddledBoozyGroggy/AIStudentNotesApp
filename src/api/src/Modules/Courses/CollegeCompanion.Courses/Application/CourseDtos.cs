using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CollegeCompanion.Courses.Domain;

namespace CollegeCompanion.Courses.Application;

/// <summary>Wire shape; matches src/web/src/lib/api/types.ts Course (plus optional archivedAt).</summary>
internal sealed record CourseResponse(
    Guid Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Term,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instructor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastSessionAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ArchivedAt)
{
    public static CourseResponse From(Course c) => new(c.Id, c.Name, c.Code, c.Term, c.Instructor, c.Color,
        c.CreatedAt, c.UpdatedAt, null /* populated once lecture sessions exist (M2) */, c.ArchivedAt);
}

internal sealed record CourseListResponse(IReadOnlyList<CourseResponse> Items, string? NextCursor);

internal sealed record CourseRequest(string? Name, string? Code, string? Term, string? Instructor, string? Color);

internal sealed record ValidCourse(string Name, string? Code, string? Term, string? Instructor, string? Color);

internal static partial class CourseValidator
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    /// <summary>Trims, turns blanks into null, enforces length limits (SEC-034). Returns field errors on failure.</summary>
    public static (ValidCourse? Value, Dictionary<string, string[]>? Errors) Validate(CourseRequest? r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r is null) return (null, new() { ["body"] = ["A JSON body is required."] });

        var name = r.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 120) errors["name"] = ["Course name must be 1 to 120 characters."];

        string? Opt(string field, string? v, int max)
        {
            var t = v?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (t.Length > max) errors[field] = [$"Must be at most {max} characters."];
            return t;
        }
        var code = Opt("code", r.Code, 32);
        var term = Opt("term", r.Term, 64);
        var instructor = Opt("instructor", r.Instructor, 120);
        var color = Opt("color", r.Color, 7);
        if (color is not null && !errors.ContainsKey("color") && !HexColor().IsMatch(color))
            errors["color"] = ["Color must look like #RRGGBB."];

        return errors.Count > 0 ? (null, errors) : (new ValidCourse(name, code, term, instructor, color), null);
    }
}
