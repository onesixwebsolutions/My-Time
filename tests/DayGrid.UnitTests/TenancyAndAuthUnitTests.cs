using DayGrid.Application.Security;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using Xunit;

namespace DayGrid.UnitTests;

internal sealed class FixedCurrentUser : ICurrentUser
{
    public FixedCurrentUser(Guid? userId) => UserId = userId;
    public Guid? UserId { get; }
}

public class TenantFilterTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    private AppDbContext As(Guid? userId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, new FixedCurrentUser(userId));

    private AppDbContext System() => TestDb.Create(_dbName);

    [Fact]
    public async Task Inserts_AreStampedWithTheCurrentUser_AndQueriesSeeOnlyOwnRows()
    {
        await using (var a = As(_alice))
        {
            a.SimpleTasks.Add(new SimpleTask { Title = "alice's" });
            await a.SaveChangesAsync();
        }
        await using (var b = As(_bob))
        {
            b.SimpleTasks.Add(new SimpleTask { Title = "bob's" });
            await b.SaveChangesAsync();
        }

        await using (var a = As(_alice))
            Assert.Equal(new[] { "alice's" }, await a.SimpleTasks.Select(t => t.Title).ToListAsync());
        await using (var b = As(_bob))
            Assert.Equal(new[] { "bob's" }, await b.SimpleTasks.Select(t => t.Title).ToListAsync());
        await using (var sys = System())
        {
            var all = await sys.SimpleTasks.ToListAsync();
            Assert.Equal(2, all.Count);
            Assert.Contains(all, t => t.UserId == _alice && t.Title == "alice's");
            Assert.Contains(all, t => t.UserId == _bob && t.Title == "bob's");
        }
    }

    [Fact]
    public async Task NoCurrentUser_SeesNothing_AndCannotInsert()
    {
        await using (var a = As(_alice))
        {
            a.Checklists.Add(new Checklist { Name = "x" });
            await a.SaveChangesAsync();
        }

        await using var anonymous = As(null);
        Assert.Empty(await anonymous.Checklists.ToListAsync());
        anonymous.Checklists.Add(new Checklist { Name = "orphan" });
        await Assert.ThrowsAsync<TenantViolationException>(() => anonymous.SaveChangesAsync());
    }

    [Fact]
    public async Task FindAsync_DoesNotReturnAnotherUsersRow()
    {
        var id = Guid.NewGuid();
        await using (var a = As(_alice))
        {
            a.Checklists.Add(new Checklist { Id = id, Name = "secret" });
            await a.SaveChangesAsync();
        }

        await using var b = As(_bob);
        Assert.Null(await b.Checklists.FindAsync(id));
    }

    [Fact]
    public async Task ForeignKey_ToAnotherUsersRow_IsRejectedAsAReference()
    {
        var alicesChecklist = new Checklist { Name = "alice" };
        await using (var a = As(_alice))
        {
            a.Checklists.Add(alicesChecklist);
            await a.SaveChangesAsync();
        }

        await using var b = As(_bob);
        b.FutureTasks.Add(new FutureTask { Title = "link", DueDate = new DateOnly(2026, 1, 1), PromoteToChecklistId = alicesChecklist.Id });
        var ex = await Assert.ThrowsAsync<TenantViolationException>(() => b.SaveChangesAsync());
        Assert.True(ex.IsReference);
    }

    [Fact]
    public async Task ForeignKey_ToOwnRowAddedInTheSameSave_IsAccepted()
    {
        await using var a = As(_alice);
        var checklist = new Checklist { Name = "mine" };
        a.Checklists.Add(checklist);
        a.ChecklistItems.Add(new ChecklistItem { ChecklistId = checklist.Id, Title = "item" });
        await a.SaveChangesAsync();
        Assert.Equal(_alice, (await a.ChecklistItems.SingleAsync()).UserId);
    }

    [Fact]
    public async Task ModifyingAForeignKey_ToAnotherUsersRow_IsRejected()
    {
        var alicesTemplate = new TimetableTemplate { Name = "alice" };
        await using (var a = As(_alice))
        {
            a.TimetableTemplates.Add(alicesTemplate);
            await a.SaveChangesAsync();
        }

        await using var b = As(_bob);
        var bobsOverride = new DayOverride { Date = new DateOnly(2026, 1, 1) };
        b.DayOverrides.Add(bobsOverride);
        await b.SaveChangesAsync();

        bobsOverride.TemplateId = alicesTemplate.Id;
        await Assert.ThrowsAsync<TenantViolationException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task ChangingTheOwner_IsRejected()
    {
        await using var a = As(_alice);
        var task = new SimpleTask { Title = "mine" };
        a.SimpleTasks.Add(task);
        await a.SaveChangesAsync();

        task.UserId = _bob;
        await Assert.ThrowsAsync<TenantViolationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task InsertingForAnotherUser_IsRejected()
    {
        await using var a = As(_alice);
        a.SimpleTasks.Add(new SimpleTask { Title = "planted", UserId = _bob });
        await Assert.ThrowsAsync<TenantViolationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task SystemContext_MayClaimLegacyRows_ButNotReassignOwnedOnes()
    {
        await using (var sys = System())
        {
            sys.SimpleTasks.Add(new SimpleTask { Title = "legacy" });
            sys.SimpleTasks.Add(new SimpleTask { Title = "owned", UserId = _bob });
            await sys.SaveChangesAsync();
        }

        await using (var sys = System())
        {
            (await sys.SimpleTasks.SingleAsync(t => t.Title == "legacy")).UserId = _alice;
            await sys.SaveChangesAsync();
        }

        await using (var sys = System())
        {
            (await sys.SimpleTasks.SingleAsync(t => t.Title == "owned")).UserId = _alice;
            await Assert.ThrowsAsync<TenantViolationException>(() => sys.SaveChangesAsync());
        }
    }

    [Fact]
    public void EveryDomainEntity_IsUserOwned()
    {
        var domainTypes = typeof(Checklist).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace == typeof(Checklist).Namespace)
            .ToList();
        Assert.NotEmpty(domainTypes);
        Assert.All(domainTypes, t => Assert.True(typeof(IUserOwned).IsAssignableFrom(t), $"{t.Name} is not IUserOwned"));
        Assert.Equal(domainTypes.Count, AppDbContext.UserOwnedTypes.Count);
    }
}

public class AppClockFactoryTests
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForTimeZone_UsesTheUsersZone_AndFallsBackToTheDefault()
    {
        var factory = new AppClockFactory(new FakeTimeProvider(Utc), "Asia/Kolkata");

        Assert.Equal(new DateOnly(2026, 10, 5), factory.ForTimeZone(null).Today);              // IST: 01:30 next day
        Assert.Equal(new DateOnly(2026, 10, 4), factory.ForTimeZone("America/New_York").Today); // EDT: 16:00
        Assert.Equal(new TimeOnly(16, 0), factory.ForTimeZone("America/New_York").TimeOfDay);
        Assert.Equal(new DateOnly(2026, 10, 5), factory.ForTimeZone("Not/AZone").Today);       // unknown -> default
    }

    [Theory]
    [InlineData("Asia/Kolkata", true)]
    [InlineData("Europe/London", true)]
    [InlineData("America/Los_Angeles", true)]
    [InlineData("UTC", true)]
    [InlineData("India Standard Time", false)] // Windows id, not IANA
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidTimeZone_AcceptsIanaIdsOnly(string? id, bool valid)
    {
        var factory = new AppClockFactory(new FakeTimeProvider(Utc), "Asia/Kolkata");
        Assert.Equal(valid, factory.IsValidTimeZone(id));
    }

    [Fact]
    public void InvalidDefaultZone_FailsAtConstruction()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => new AppClockFactory(new FakeTimeProvider(Utc), "Mars/Olympus_Mons"));
    }
}

public class EmailTemplateAndPickupTests
{
    [Fact]
    public void Templates_HtmlEncodeUserSuppliedValues()
    {
        var content = EmailTemplates.ResetPassword("<script>alert(1)</script>", "https://x.test/reset-password#email=a&token=abc");

        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
        Assert.Contains("https://x.test/reset-password#email=a&amp;token=abc", content.HtmlBody);
    }

    [Fact]
    public void EmailsBeforeConfirmation_UseANeutralGreeting()
    {
        // The confirmation email never carries a name (it was typed by whoever registered).
        var confirm = EmailTemplates.ConfirmEmail("https://x.test/confirm-email#userId=1&token=abc");
        Assert.Contains("<p>Hello,</p>", confirm.HtmlBody);
        Assert.DoesNotContain("Hi ", confirm.HtmlBody);

        Assert.Contains("<p>Hello,</p>", EmailTemplates.AlreadyRegistered(null, "https://x.test/login", "https://x.test/forgot-password").HtmlBody);
        Assert.Contains("<p>Hello,</p>", EmailTemplates.PasswordChanged(null, "https://x.test/forgot-password").HtmlBody);
        Assert.Contains("<p>Hi Ann,</p>", EmailTemplates.PasswordChanged("Ann", "https://x.test/forgot-password").HtmlBody);
    }

    [Fact]
    public async Task PickupSender_WritesOneEmlFilePerMessage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "daygrid-pickup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sender = new PickupDirectoryEmailSender(
                Options.Create(new EmailSettings { Mode = EmailDeliveryMode.Pickup, PickupDirectory = dir, FromAddress = "no-reply@daygrid.test" }),
                NullLogger<PickupDirectoryEmailSender>.Instance);

            await sender.SendAsync("someone@example.test", "Hello", "<p>Body</p>");
            await sender.SendAsync("other@example.test", "Second", "<p>Two</p>");

            var files = Directory.GetFiles(dir, "*.eml").OrderBy(f => f).ToList();
            Assert.Equal(2, files.Count);
            var message = await MimeMessage.LoadAsync(files[0]);
            Assert.Equal("Hello", message.Subject);
            Assert.Equal("someone@example.test", message.To.Mailboxes.Single().Address);
            Assert.Equal("no-reply@daygrid.test", message.From.Mailboxes.Single().Address);
            Assert.Contains("<p>Body</p>", message.HtmlBody);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task PickupSender_WithoutDirectory_ThrowsEmailNotConfigured()
    {
        var sender = new PickupDirectoryEmailSender(
            Options.Create(new EmailSettings { Mode = EmailDeliveryMode.Pickup }), NullLogger<PickupDirectoryEmailSender>.Instance);
        await Assert.ThrowsAsync<EmailNotConfiguredException>(() => sender.SendAsync("a@example.test", "s", "b"));
    }
}
