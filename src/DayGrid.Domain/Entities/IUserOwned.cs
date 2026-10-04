namespace DayGrid.Domain.Entities;

/// <summary>
/// A row that belongs to exactly one user account. AppDbContext filters every query on these
/// entities to the current user and stamps <see cref="UserId"/> on insert.
/// </summary>
public interface IUserOwned
{
    Guid? UserId { get; set; }
}
