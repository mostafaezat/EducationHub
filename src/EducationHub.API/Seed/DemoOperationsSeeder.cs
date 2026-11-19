using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Development demo data for the operational modules (Mission Phases 3-6) in the
/// <c>admin-ui-test</c> tenant: students and parents, three study groups with weekly slots and
/// enrollments, past and upcoming sessions with attendance, fee rules, two months of charges,
/// payments, cash accounts and movements, expenses, teacher compensation and a settlement,
/// homework with submissions, exams with results, and in-app notifications.
/// </summary>
/// <remarks>
/// <para>
/// Add-only and run-once: the whole set is written in one save, and if any group with a
/// <c>DEMO-</c> code already exists (even soft-deleted) nothing is written at all, so a
/// restart never duplicates or alters data. It never creates sign-in accounts or passwords:
/// students and parents get profiles only, and existing demo accounts (seeded by
/// <see cref="DemoStudentProfileSeeder"/>) are enrolled so their notification bell has content.
/// Teachers are the tenant's existing teacher profiles; without one, groups have no teacher and
/// no compensation is seeded.
/// </para>
/// <para>
/// Money is internally consistent: every charge's <c>AmountPaid</c> equals its allocations,
/// every payment, expense and teacher payment has exactly one cash posting, and teacher
/// payments settle exactly the compensations they pay for.
/// </para>
/// </remarks>
public sealed class DemoOperationsSeeder(
    EducationHubDbContext context,
    ILogger<DemoOperationsSeeder> logger)
{
    public const string GroupCodePrefix = "DEMO-";

    private static readonly string[] StudentNames =
    [
        "Ahmed Hassan", "Mariam Adel", "Youssef Kamal", "Salma Mahmoud", "Omar Khaled", "Nour El-Din Samir",
        "Hana Mostafa", "Karim Fathy", "Laila Ibrahim", "Ziad Ashraf", "Farida Tarek", "Adam Sherif"
    ];

    private static readonly string[] ParentNames =
    [
        "Hassan Ali", "Adel Mansour", "Kamal Youssef", "Mahmoud Saad", "Khaled Omar", "Samir Nabil"
    ];

    /// <summary>
    /// The mobile numbers of the account-less demo parent profiles (01000000001 ... 01000000006).
    /// Other demo seeders must not hand these out again: see DemoStaffSeeder.DemoParentPhones.
    /// </summary>
    public static IReadOnlyList<string> DemoParentProfilePhones { get; } =
        [.. Enumerable.Range(0, ParentNames.Length).Select(static p => $"0100000{p + 1:D4}")];

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(AcademicSeeder.DemoTenantSlug, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

    public async Task SeedAsync(string tenantSlug, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var tenant = await context.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug && !t.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogInformation("No active tenant with slug {Slug}; demo operations seed skipped.", tenantSlug);
            return;
        }

        var tenantId = tenant.Id;
        var plan = new Plan(today);

        var alreadySeeded = await context.StudyGroups.IgnoreQueryFilters()
            .AnyAsync(g => g.TenantId == tenantId && g.Code != null && g.Code.StartsWith(GroupCodePrefix), cancellationToken)
            .ConfigureAwait(false);

        if (alreadySeeded)
        {
            logger.LogInformation("Demo operations data already present for {Slug}; nothing written.", tenantSlug);
            return;
        }

        var subjects = await context.Subjects.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted && s.IsActive)
            .OrderBy(static s => s.SortOrder).ThenBy(static s => s.Name)
            .Take(3).ToListAsync(cancellationToken).ConfigureAwait(false);

        if (subjects.Count == 0)
        {
            logger.LogWarning("Tenant {Slug} has no active subject; demo operations seed skipped.", tenantSlug);
            return;
        }

        var grades = await context.GradeLevels.IgnoreQueryFilters()
            .Where(g => g.TenantId == tenantId && !g.IsDeleted && g.IsActive)
            .OrderBy(static g => g.Order).Select(static g => g.Id)
            .Take(3).ToListAsync(cancellationToken).ConfigureAwait(false);

        // The demo teacher accounts (DemoTeacherSeeder) get teacher profiles if they have none,
        // so the demo groups have teachers and compensation on a fresh database.
        for (var t = 0; t < DemoTeacherSeeder.DemoTeacherEmails.Count; t++)
        {
            var normalized = DemoTeacherSeeder.DemoTeacherEmails[t].ToUpperInvariant();
            var userId = await context.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && u.NormalizedEmail == normalized)
                .Select(static u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (userId is not { } id
                || await context.TeacherProfiles.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId && p.UserId == id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var profile = new TeacherProfile
            {
                TenantId = tenantId,
                UserId = id,
                EmployeeNumber = $"DEMO-T{t + 1:D2}",
                HireDate = plan.PreviousMonthStart.AddMonths(-6),
                Notes = "Demo teacher"
            };

            context.TeacherProfiles.Add(profile);
            context.TeacherSubjects.Add(new TeacherSubject { TenantId = tenantId, TeacherProfileId = profile.Id, SubjectId = subjects[t % subjects.Count].Id });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var teachers = await context.TeacherProfiles.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && !t.IsDeleted && t.IsActive)
            .OrderBy(static t => t.CreatedAt).Select(static t => t.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // ---- People ------------------------------------------------------------------
        var students = new List<StudentProfile>();

        for (var i = 0; i < StudentNames.Length; i++)
        {
            var number = $"DEMO-S{i + 1:D2}";
            var existing = await context.StudentProfiles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StudentNumber == number, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                if (!existing.IsDeleted)
                {
                    students.Add(existing);
                }

                continue;
            }

            var student = new StudentProfile
            {
                TenantId = tenantId,
                FullName = StudentNames[i],
                StudentNumber = number,
                GradeLevelId = grades.Count == 0 ? null : grades[i % grades.Count],
                Status = StudentStatus.Active,
                EnrollmentDate = plan.PreviousMonthStart,
                DateOfBirth = new DateOnly(2010 + (i % 4), (i % 12) + 1, (i % 27) + 1)
            };

            context.StudentProfiles.Add(student);
            students.Add(student);
        }

        for (var p = 0; p < ParentNames.Length; p++)
        {
            var email = $"demo.parent{p + 1:D2}@educationhub.local";
            var exists = await context.ParentProfiles.IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == tenantId && x.Email == email, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                continue;
            }

            var parent = new ParentProfile
            {
                TenantId = tenantId,
                FullName = ParentNames[p],
                Email = email,
                PhoneNumber = DemoParentProfilePhones[p]
            };

            context.ParentProfiles.Add(parent);

            foreach (var child in students.Skip(p * 2).Take(2).Where(static s => s.UserId == null))
            {
                context.StudentGuardians.Add(new StudentGuardian
                {
                    TenantId = tenantId,
                    StudentProfileId = child.Id,
                    ParentProfileId = parent.Id,
                    Relationship = GuardianRelationship.Father,
                    IsPrimary = true
                });
            }
        }

        // Students that have a sign-in account (demo accounts) join group A so their bell fills.
        var accountStudents = await context.StudentProfiles.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted && s.UserId != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // ---- Groups, slots, enrollments -------------------------------------------------
        var groupSpecs = new[]
        {
            new GroupSpec("A", 0, 5, [(DayOfWeek.Sunday, new TimeOnly(16, 0)), (DayOfWeek.Tuesday, new TimeOnly(16, 0))], 90, "Room 1"),
            new GroupSpec("B", 4, 5, [(DayOfWeek.Monday, new TimeOnly(17, 0)), (DayOfWeek.Wednesday, new TimeOnly(17, 0))], 90, "Room 2"),
            new GroupSpec("C", 8, 4, [(DayOfWeek.Saturday, new TimeOnly(10, 0))], 120, "Lab")
        };

        var groups = new List<(StudyGroup Group, List<StudentProfile> Roster, List<ScheduleSlot> Slots)>();

        for (var g = 0; g < groupSpecs.Length; g++)
        {
            var spec = groupSpecs[g];
            var subject = subjects[g % subjects.Count];
            var group = new StudyGroup
            {
                TenantId = tenantId,
                Name = $"{subject.Name} - Group {spec.Letter}",
                Code = $"{GroupCodePrefix}G{g + 1}",
                SubjectId = subject.Id,
                GradeLevelId = grades.Count == 0 ? null : grades[g % grades.Count],
                TeacherProfileId = teachers.Count == 0 ? null : teachers[g % teachers.Count],
                Capacity = 15,
                Notes = "Demo group"
            };

            context.StudyGroups.Add(group);

            var roster = students.Skip(spec.FirstStudent).Take(spec.StudentCount).ToList();

            if (g == 0)
            {
                roster.AddRange(accountStudents.Where(a => roster.All(r => r.Id != a.Id)));
            }

            foreach (var student in roster)
            {
                context.Enrollments.Add(new Enrollment
                {
                    TenantId = tenantId,
                    StudyGroupId = group.Id,
                    StudentProfileId = student.Id,
                    EnrolledOn = plan.PreviousMonthStart,
                    Status = EnrollmentStatus.Active
                });
            }

            var slots = spec.Slots.Select(s => new ScheduleSlot
                {
                    TenantId = tenantId,
                    StudyGroupId = group.Id,
                    DayOfWeek = s.Day,
                    StartTime = s.Start,
                    EndTime = s.Start.AddMinutes(spec.Minutes),
                    Room = spec.Room
                })
                .ToList();

            context.ScheduleSlots.AddRange(slots);
            groups.Add((group, roster, slots));
        }

        // ---- Sessions + attendance (four weeks back, two ahead) -----------------------
        var completed = new List<ClassSession>();
        var sessionCounter = 0;

        foreach (var (group, roster, slots) in groups)
        {
            var lesson = 0;

            for (var day = today.AddDays(-28); day <= today.AddDays(14); day = day.AddDays(1))
            {
                foreach (var slot in slots.Where(s => s.DayOfWeek == day.DayOfWeek))
                {
                    lesson++;
                    sessionCounter++;
                    var past = day < today;
                    var cancelled = past && sessionCounter % 11 == 0;
                    var session = new ClassSession
                    {
                        TenantId = tenantId,
                        StudyGroupId = group.Id,
                        ScheduleSlotId = slot.Id,
                        Date = day,
                        StartTime = slot.StartTime,
                        EndTime = slot.EndTime,
                        TeacherProfileId = group.TeacherProfileId,
                        Status = cancelled ? SessionStatus.Cancelled : past ? SessionStatus.Completed : SessionStatus.Scheduled,
                        Topic = $"Lesson {lesson}",
                        Notes = cancelled ? "Cancelled (holiday)" : null
                    };

                    context.ClassSessions.Add(session);

                    if (!past || cancelled)
                    {
                        continue;
                    }

                    completed.Add(session);

                    for (var s = 0; s < roster.Count; s++)
                    {
                        var key = sessionCounter + s;
                        context.SessionAttendances.Add(new SessionAttendance
                        {
                            TenantId = tenantId,
                            ClassSessionId = session.Id,
                            StudentProfileId = roster[s].Id,
                            Status = key % 9 == 0 ? AttendanceStatus.Absent
                                : key % 7 == 0 ? AttendanceStatus.Late
                                : key % 13 == 0 ? AttendanceStatus.Excused
                                : AttendanceStatus.Present,
                            Notes = key % 9 == 0 ? "No notice" : null
                        });
                    }
                }
            }
        }

        // ---- Cash accounts ------------------------------------------------------------
        var accounts = await context.CashAccounts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && !a.IsDeleted && a.IsActive)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var cashBox = accounts.FirstOrDefault(static a => a.IsDefault) ?? accounts.FirstOrDefault();

        if (cashBox is null)
        {
            cashBox = new CashAccount { TenantId = tenantId, Name = "Main cash box", Type = CashAccountType.Cash, OpeningBalance = 5000m, IsDefault = true };
            context.CashAccounts.Add(cashBox);
            context.CashAccounts.Add(new CashAccount { TenantId = tenantId, Name = "Bank account", Type = CashAccountType.Bank, OpeningBalance = 20000m });
        }

        void Post(CashDirection direction, decimal amount, DateOnly on, CashSourceType source, Guid sourceId, string description)
            => context.CashTransactions.Add(new CashTransaction
            {
                TenantId = tenantId,
                CashAccountId = cashBox.Id,
                Direction = direction,
                Amount = amount,
                OccurredOn = on,
                SourceType = source,
                SourceId = sourceId,
                Description = description
            });

        Post(CashDirection.In, 1000m, plan.PreviousMonthStart, CashSourceType.Manual, Guid.NewGuid(), "Owner deposit (demo)");

        // ---- Fee rules + charges --------------------------------------------------------
        var rules = await context.FeeRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && !r.IsDeleted && r.IsActive && r.Frequency == FeeFrequency.Monthly)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (rules.Count == 0)
        {
            var defaultRule = new FeeRule { TenantId = tenantId, Name = "Monthly tuition", Amount = 400m, Frequency = FeeFrequency.Monthly, Notes = "Demo default" };
            var groupRule = new FeeRule { TenantId = tenantId, Name = $"{groups[0].Group.Name} tuition", Amount = 500m, Frequency = FeeFrequency.Monthly, StudyGroupId = groups[0].Group.Id };
            context.FeeRules.AddRange(defaultRule, groupRule);
            rules.AddRange([defaultRule, groupRule]);
        }

        FeeRule? RuleFor(StudyGroup group)
            => rules.FirstOrDefault(r => r.StudyGroupId == group.Id)
               ?? rules.FirstOrDefault(r => r.StudyGroupId == null && r.GradeLevelId == group.GradeLevelId && group.GradeLevelId != null)
               ?? rules.FirstOrDefault(static r => r.StudyGroupId == null && r.GradeLevelId == null);

        var chargesByStudent = new Dictionary<Guid, List<Charge>>();

        foreach (var (group, roster, _) in groups)
        {
            var rule = RuleFor(group);
            var amount = rule?.Amount ?? 400m;

            foreach (var period in new[] { plan.PreviousMonthStart, plan.MonthStart })
            {
                foreach (var student in roster)
                {
                    var charge = new Charge
                    {
                        TenantId = tenantId,
                        StudentProfileId = student.Id,
                        FeeRuleId = rule?.Id,
                        StudyGroupId = group.Id,
                        Description = $"{group.Name} - {period:yyyy-MM}",
                        Amount = amount,
                        DueDate = period.AddDays(4),
                        PeriodKey = period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
                    };

                    context.Charges.Add(charge);

                    if (!chargesByStudent.TryGetValue(student.Id, out var list))
                    {
                        chargesByStudent[student.Id] = list = [];
                    }

                    list.Add(charge);
                }
            }
        }

        // ---- Payments (FIFO allocation, one cash posting each) --------------------------
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.BankTransfer, PaymentMethod.Cash };
        var paymentNo = 0;
        var studentIndex = 0;

        foreach (var (studentId, charges) in chargesByStudent)
        {
            var ordered = charges.OrderBy(static c => c.DueDate).ToList();
            var previous = ordered.Where(c => c.DueDate < plan.MonthStart).Sum(static c => c.Amount);
            var current = ordered.Where(c => c.DueDate >= plan.MonthStart).Sum(static c => c.Amount);

            // 0: owes everything, 1: paid last month, 2: paid both months, 3: last month + half of this one.
            var payments = (studentIndex++ % 4) switch
            {
                1 => new[] { (previous, plan.PreviousMonthStart.AddDays(6)) },
                2 => [(previous, plan.PreviousMonthStart.AddDays(5)), (current, plan.Clamp(plan.MonthStart.AddDays(3)))],
                3 => [(previous, plan.PreviousMonthStart.AddDays(8)), (Math.Round(current / 2, 2), plan.Clamp(plan.MonthStart.AddDays(2)))],
                _ => []
            };

            foreach (var (amount, paidOn) in payments.Where(static p => p.Item1 > 0))
            {
                paymentNo++;
                var payment = new Payment
                {
                    TenantId = tenantId,
                    StudentProfileId = studentId,
                    CashAccountId = cashBox.Id,
                    Amount = amount,
                    PaidOn = paidOn,
                    Method = methods[paymentNo % methods.Length],
                    Reference = $"DEMO-PAY-{paymentNo:D3}"
                };

                context.Payments.Add(payment);
                var left = amount;

                foreach (var charge in ordered.Where(static c => c.AmountPaid < c.Amount))
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    var applied = Math.Min(left, charge.Amount - charge.AmountPaid);
                    charge.AmountPaid += applied;
                    charge.Status = charge.AmountPaid >= charge.Amount ? ChargeStatus.Paid : ChargeStatus.PartiallyPaid;
                    left -= applied;
                    context.PaymentAllocations.Add(new PaymentAllocation { TenantId = tenantId, PaymentId = payment.Id, ChargeId = charge.Id, Amount = applied });
                }

                Post(CashDirection.In, amount, paidOn, CashSourceType.Payment, payment.Id, $"Payment {payment.Reference}");
            }
        }

        // ---- Expenses -------------------------------------------------------------------
        var categoryNames = new[] { "Rent", "Utilities", "Supplies", "Maintenance" };
        var categories = new Dictionary<string, Guid>();

        foreach (var name in categoryNames)
        {
            var existing = await context.ExpenseCategories.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && !c.IsDeleted && c.Name == name)
                .Select(static c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (existing is { } id)
            {
                categories[name] = id;
                continue;
            }

            var category = new ExpenseCategory { TenantId = tenantId, Name = name };
            context.ExpenseCategories.Add(category);
            categories[name] = category.Id;
        }

        var expenses = new (string Category, decimal Amount, DateOnly On, string Description)[]
        {
            ("Rent", 3000m, plan.PreviousMonthStart.AddDays(1), "Centre rent (last month)"),
            ("Utilities", 450m, plan.PreviousMonthStart.AddDays(14), "Electricity and water"),
            ("Rent", 3000m, plan.Clamp(plan.MonthStart.AddDays(1)), "Centre rent (this month)"),
            ("Supplies", 260m, plan.Clamp(plan.MonthStart.AddDays(2)), "Markers and paper"),
            ("Maintenance", 600m, plan.PreviousMonthStart.AddDays(20), "Air-conditioner service")
        };

        for (var e = 0; e < expenses.Length; e++)
        {
            var (category, amount, on, description) = expenses[e];
            var expense = new Expense
            {
                TenantId = tenantId,
                ExpenseCategoryId = categories[category],
                CashAccountId = cashBox.Id,
                Amount = amount,
                SpentOn = on,
                Description = description,
                Reference = $"DEMO-EXP-{e + 1:D2}"
            };

            context.Expenses.Add(expense);
            Post(CashDirection.Out, amount, on, CashSourceType.Expense, expense.Id, description);
        }

        // ---- Teacher compensation + one settlement ---------------------------------------
        var hasRule = await context.CompensationRules.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantId && !r.IsDeleted && r.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (!hasRule && teachers.Count > 0)
        {
            var rate = new CompensationRule { TenantId = tenantId, Rate = 150m, RateBasis = CompensationRateBasis.PerSession };
            context.CompensationRules.Add(rate);

            var compensations = completed
                .Where(static s => s.TeacherProfileId != null)
                .Select(s => new SessionCompensation
                {
                    TenantId = tenantId,
                    ClassSessionId = s.Id,
                    TeacherProfileId = s.TeacherProfileId!.Value,
                    CompensationRuleId = rate.Id,
                    Amount = rate.Rate
                })
                .ToList();

            context.SessionCompensations.AddRange(compensations);

            // Settle the first teacher's sessions from before this month.
            var teacherId = teachers[0];
            var sessionDates = completed.ToDictionary(static s => s.Id, static s => s.Date);
            var settled = compensations.Where(c => c.TeacherProfileId == teacherId && sessionDates[c.ClassSessionId] < plan.MonthStart).ToList();

            if (settled.Count > 0)
            {
                var paidOn = plan.Clamp(plan.MonthStart.AddDays(1));
                var teacherPayment = new TeacherPayment
                {
                    TenantId = tenantId,
                    TeacherProfileId = teacherId,
                    CashAccountId = cashBox.Id,
                    Amount = settled.Sum(static c => c.Amount),
                    PaidOn = paidOn,
                    PeriodFrom = settled.Min(c => sessionDates[c.ClassSessionId]),
                    PeriodTo = plan.MonthStart.AddDays(-1),
                    Notes = "Demo settlement"
                };

                context.TeacherPayments.Add(teacherPayment);

                foreach (var c in settled)
                {
                    c.TeacherPaymentId = teacherPayment.Id;
                }

                Post(CashDirection.Out, teacherPayment.Amount, paidOn, CashSourceType.TeacherPayment, teacherPayment.Id, "Teacher payment");
            }
        }

        // ---- Homework + exams ----------------------------------------------------------------
        var scoreSeed = 0;

        foreach (var (group, roster, _) in groups)
        {
            var graded = new Homework
            {
                TenantId = tenantId,
                StudyGroupId = group.Id,
                Title = "Worksheet 1",
                Description = "Exercises 1-10 from the chapter review.",
                AssignedOn = today.AddDays(-10),
                DueDate = today.AddDays(-3),
                MaxGrade = 10m
            };

            var open = new Homework
            {
                TenantId = tenantId,
                StudyGroupId = group.Id,
                Title = "Worksheet 2",
                Description = "Prepare the next chapter's vocabulary.",
                AssignedOn = today.AddDays(-2),
                DueDate = today.AddDays(5),
                MaxGrade = 10m
            };

            context.Homeworks.AddRange(graded, open);

            for (var s = 0; s < roster.Count; s++)
            {
                var missing = s == roster.Count - 1;
                context.HomeworkSubmissions.Add(new HomeworkSubmission
                {
                    TenantId = tenantId,
                    HomeworkId = graded.Id,
                    StudentProfileId = roster[s].Id,
                    Status = missing ? SubmissionStatus.Missing : s == 1 ? SubmissionStatus.Submitted : SubmissionStatus.Graded,
                    SubmittedAt = missing ? null : today.AddDays(-4).ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc),
                    Grade = missing || s == 1 ? null : 6m + ((s + scoreSeed) % 5),
                    Feedback = missing ? null : s % 2 == 0 ? "Good work" : null
                });

                if (s < 2)
                {
                    context.HomeworkSubmissions.Add(new HomeworkSubmission
                    {
                        TenantId = tenantId,
                        HomeworkId = open.Id,
                        StudentProfileId = roster[s].Id,
                        Status = SubmissionStatus.Submitted,
                        SubmittedAt = today.AddDays(-1).ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc)
                    });
                }
            }

            var exam = new Exam { TenantId = tenantId, StudyGroupId = group.Id, Title = "Monthly quiz", ExamDate = today.AddDays(-7), MaxScore = 50m, Notes = "Chapters 1-2" };
            var upcoming = new Exam { TenantId = tenantId, StudyGroupId = group.Id, Title = "Midterm", ExamDate = today.AddDays(10), MaxScore = 100m };
            context.Exams.AddRange(exam, upcoming);

            for (var s = 0; s < roster.Count; s++)
            {
                var absent = s == 2;
                context.ExamResults.Add(new ExamResult
                {
                    TenantId = tenantId,
                    ExamId = exam.Id,
                    StudentProfileId = roster[s].Id,
                    IsAbsent = absent,
                    Score = absent ? null : 30m + ((s * 7 + scoreSeed) % 21),
                    Notes = absent ? "Sick" : null
                });
            }

            scoreSeed += 3;
        }

        // ---- Notifications for the demo accounts (and their parents) in group A ----------------
        var groupA = groups[0];
        var studentUserIds = groupA.Roster.Where(static s => s.UserId != null).Select(static s => s.UserId!.Value).ToList();
        var accountStudentIds = groupA.Roster.Where(static s => s.UserId != null).Select(static s => s.Id).ToList();
        var parentUserIds = await context.StudentGuardians.IgnoreQueryFilters()
            .Where(g => g.TenantId == tenantId && !g.IsDeleted && accountStudentIds.Contains(g.StudentProfileId))
            .Join(context.ParentProfiles.IgnoreQueryFilters().Where(static p => p.UserId != null && !p.IsDeleted), g => g.ParentProfileId, p => p.Id, (_, p) => p.UserId!.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var notified = 0;

        foreach (var userId in studentUserIds.Concat(parentUserIds).Distinct())
        {
            context.Notifications.AddRange(
                new Notification { TenantId = tenantId, UserId = userId, Title = "New homework: Worksheet 2", Body = $"{groupA.Group.Name} - due {today.AddDays(5):yyyy-MM-dd}", Link = "/my-family" },
                new Notification { TenantId = tenantId, UserId = userId, Title = "Exam result: Monthly quiz", Body = $"{groupA.Group.Name}", Link = "/my-family" },
                new Notification { TenantId = tenantId, UserId = userId, Title = "New homework: Worksheet 1", Body = $"{groupA.Group.Name}", Link = "/my-family", IsRead = true, ReadAt = DateTime.UtcNow.AddDays(-9) });
            notified++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Demo operations seed for {Slug}: {Groups} groups, {Students} students, {Sessions} sessions, {Payments} payments, {Notified} notified account(s).",
            tenantSlug,
            groups.Count,
            students.Count,
            sessionCounter,
            paymentNo,
            notified);
    }

    private sealed record GroupSpec(string Letter, int FirstStudent, int StudentCount, (DayOfWeek Day, TimeOnly Start)[] Slots, int Minutes, string Room);

    private sealed class Plan(DateOnly today)
    {
        public DateOnly MonthStart { get; } = new(today.Year, today.Month, 1);

        public DateOnly PreviousMonthStart => MonthStart.AddMonths(-1);

        /// <summary>Never dates money in the future.</summary>
        public DateOnly Clamp(DateOnly date) => date > today ? today : date;
    }
}
