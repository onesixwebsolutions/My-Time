using DayGrid.Application.Dtos;

namespace DayGrid.Application.Scheduling;

/// <summary>
/// Builds the merged, single-day view (<see cref="Dtos.TodayDto"/>) out of the timetable,
/// checklists and future tasks for a given date.
///
/// The interface lives here in Application so both the API layer (which only needs to depend on
/// Application's abstractions) and tests can reference it without pulling in Infrastructure/EF
/// Core. The concrete implementation, <c>DayPlanBuilder</c>, lives in
/// <c>DayGrid.Infrastructure/Scheduling/DayPlanBuilder.cs</c> because it needs <c>AppDbContext</c>
/// — Application intentionally has no project reference to Infrastructure, so it cannot define
/// the implementation itself.
/// </summary>
public interface IDayPlanBuilder
{
    /// <summary>Builds the full Today view for <paramref name="date"/>.</summary>
    Task<TodayDto> BuildAsync(DateOnly date, CancellationToken ct = default);
}
