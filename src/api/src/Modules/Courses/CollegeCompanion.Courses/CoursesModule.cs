using CollegeCompanion.Courses.Application;
using CollegeCompanion.Courses.Contracts;
using CollegeCompanion.Courses.Persistence;
using CollegeCompanion.SharedKernel;
using CollegeCompanion.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CollegeCompanion.Courses;

/// <summary>Public surface of the Courses module. Everything else in this assembly is internal.</summary>
public static class CoursesModule
{
    public static WebApplicationBuilder AddCoursesModule(this WebApplicationBuilder builder)
    {
        var cs = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        builder.Services.AddDbContext<CoursesDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", CoursesDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));
        builder.Services.AddScoped<ICourseOwnership, CourseOwnership>();
        return builder;
    }

    public static IEndpointRouteBuilder MapCoursesEndpoints(this IEndpointRouteBuilder api)
    {
        CourseEndpoints.Map(api);
        return api;
    }

    public static async Task MigrateCoursesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CoursesDbContext>().Database.MigrateAsync(ct);
    }
}

internal sealed class CourseOwnership(CoursesDbContext db, ICurrentUser currentUser) : ICourseOwnership
{
    public async Task<bool> IsOwnedByAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        if (currentUser.UserId != userId) return false;
        return await db.Courses.AnyAsync(c => c.Id == courseId && c.UserId == userId, ct);
    }
}
