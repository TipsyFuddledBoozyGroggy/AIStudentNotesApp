using Tether.Courses.Domain;
using Tether.Courses.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Tether.Courses.Application;

internal static class CourseEndpoints
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/courses"); // authenticated by the fallback policy (SEC-001)
        g.MapPost("/", Create);
        g.MapGet("/", List);
        g.MapGet("/{id:guid}", Get);
        g.MapPut("/{id:guid}", Update);
        g.MapDelete("/{id:guid}", Delete);
    }

    /// <summary>UTC now truncated to microseconds so responses match what PostgreSQL stores.</summary>
    private static DateTimeOffset Now(TimeProvider clock)
    {
        var t = clock.GetUtcNow();
        return new DateTimeOffset(t.Ticks - t.Ticks % 10, TimeSpan.Zero);
    }



    private static async Task<IResult> Create(CourseRequest? body, CoursesDbContext db, TimeProvider clock, ETagService etags,
        HttpContext http, CancellationToken ct)
    {
        var (v, errors) = CourseValidator.Validate(body);
        if (v is null) return Problems.Validation(errors!);

        var now = Now(clock);
        // UserId is deliberately left empty: the ownership interceptor stamps it from the session.
        var course = new Course
        {
            Id = Ids.New(), Name = v.Name, Code = v.Code, Term = v.Term, Instructor = v.Instructor, Color = v.Color,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        http.Response.Headers.ETag = etags.For(course);
        return Results.Created($"/api/v1/courses/{course.Id}", CourseResponse.From(course));
    }

    private static async Task<IResult> List(string? cursor, int? limit, bool? includeArchived, CoursesDbContext db,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit) errors["limit"] = [$"Limit must be between 1 and {MaxLimit}."];
        Guid? after = null;
        if (cursor is not null)
        {
            if (Guid.TryParseExact(cursor, "N", out var g)) after = g;
            else errors["cursor"] = ["Cursor is not valid."];
        }
        if (errors.Count > 0) return Problems.Validation(errors);

        var q = db.Courses.AsNoTracking();
        if (includeArchived != true) q = q.Where(c => c.ArchivedAt == null);
        // Ids are UUIDv7 (time ordered), so id order is creation order and a stable keyset.
        if (after is { } a) q = q.Where(c => c.Id.CompareTo(a) < 0);
        var rows = await q.OrderByDescending(c => c.Id).Take(take + 1).ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var next = hasMore ? rows[^1].Id.ToString("N") : null;
        return Results.Ok(new CourseListResponse(rows.Select(CourseResponse.From).ToList(), next));
    }

    private static async Task<IResult> Get(Guid id, CoursesDbContext db, ETagService etags, HttpContext http, CancellationToken ct)
    {
        // Query filter scopes to the caller: foreign and missing ids are indistinguishable (ADR-002).
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null) return Problems.NotFound("Course not found.");
        http.Response.Headers.ETag = etags.For(course);
        return Results.Ok(CourseResponse.From(course));
    }

    private static async Task<IResult> Update(Guid id, CourseRequest? body, CoursesDbContext db, TimeProvider clock, ETagService etags,
        HttpContext http, CancellationToken ct)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null) return Problems.NotFound("Course not found.");

        var ifMatch = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch)) return Problems.PreconditionRequired();

        var (v, errors) = CourseValidator.Validate(body);
        if (v is null) return Problems.Validation(errors!);

        if (!etags.Matches(ifMatch, course)) return Problems.PreconditionFailed();

        course.Name = v.Name; course.Code = v.Code; course.Term = v.Term;
        course.Instructor = v.Instructor; course.Color = v.Color;
        course.UpdatedAt = Now(clock);
        // Make the UPDATE itself conditional on the version the client saw (closes the read-write race).
        db.Entry(course).Property(c => c.Version).OriginalValue = course.Version;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictOrGoneAsync(db, id, ct);
        }
        http.Response.Headers.ETag = etags.For(course);
        return Results.Ok(CourseResponse.From(course));
    }

    /// <summary>DELETE archives (soft delete). <c>?purge=true</c> permanently deletes the course.</summary>
    private static async Task<IResult> Delete(Guid id, bool? purge, CoursesDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null) return Problems.NotFound("Course not found.");

        if (purge == true)
        {
            db.Courses.Remove(course); // M2: cascade to sessions/transcripts via the Lectures module
        }
        else if (course.ArchivedAt is null)
        {
            course.ArchivedAt = Now(clock);
            course.UpdatedAt = course.ArchivedAt.Value;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Raced with another update (409) or delete (404). Never a 500.
            return await ConflictOrGoneAsync(db, id, ct);
        }
        return Results.NoContent();
    }

    /// <summary>After a lost optimistic-concurrency race: 404 if the row is gone, otherwise 409.</summary>
    private static async Task<IResult> ConflictOrGoneAsync(CoursesDbContext db, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var stillThere = await db.Courses.AsNoTracking().AnyAsync(c => c.Id == id, ct);
        return stillThere ? Problems.ConcurrencyConflict() : Problems.NotFound("Course not found.");
    }
}
