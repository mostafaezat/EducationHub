using System.Reflection;
using EducationHub.Application.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Application;

/// <summary>Wires the Application layer into the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR, FluentValidation and the cross-cutting pipeline behaviours.
    /// </summary>
    /// <remarks>
    /// Behaviour order is significant and is asserted by
    /// <c>ApplicationRegistrationTests.Behaviours_AreRegisteredInTheIntendedOrder</c>:
    /// logging opens the trace, then business-rule mapping converts an expected failure
    /// into a result, and only then does validation reject the request. Validation runs last
    /// so a malformed request is never logged as "handling" business work.
    /// </remarks>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(BusinessRuleExceptionBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Concrete support types, so a handler can share role resolution without depending on
        // another handler class - which MediatR never registers.
        services.AddScoped<Features.Users.Commands.UserCommandSupport>();

        // Writes to one tenant's system roles; shared by the seeder and by tenant provisioning
        // so the seed-time and runtime definitions of a role can never disagree.
        services.AddScoped<Features.Tenancy.Services.TenantRoleProvisioner>();

        // The compensation precedence ladder, used by the teacher-compensation query.
        services.AddScoped<Features.Teachers.Services.CompensationResolver>();

        // Students module (Mission Phase 2.5): shared DTO builder and the optional-account
        // link/create step used by the student and parent create commands.
        services.AddScoped<Features.Students.Services.StudentReadModel>();
        services.AddScoped<Features.Students.Services.ProfileAccountSupport>();

        // Scheduling (Mission Phase 3).
        services.AddScoped<Features.Scheduling.SchedulingReadModel>();
        services.AddScoped<Features.Scheduling.SessionMaterializer>();

        // Finance (Mission Phases 4-5).
        services.AddScoped<Features.Finance.FinanceReadModel>();
        services.AddScoped<Features.Finance.CashPostingService>();
        services.AddScoped<Features.Finance.FeeEngine>();
        services.AddScoped<Features.Finance.CompensationEngine>();

        // Homework, exams and in-app notifications (Mission Phase 6).
        services.AddScoped<Features.Learning.NotificationService>();

        return services;
    }
}
