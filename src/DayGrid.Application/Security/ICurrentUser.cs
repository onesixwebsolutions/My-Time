namespace DayGrid.Application.Security;

/// <summary>
/// The account the current unit of work acts for. In a request it is the authenticated user
/// (from the auth cookie); in a background job it is whichever user the job explicitly
/// impersonates for that iteration. <c>null</c> means "nobody" — tenant-filtered queries then
/// return no rows at all.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
