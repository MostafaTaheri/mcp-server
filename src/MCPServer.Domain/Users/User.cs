using MCPServer.Domain.Abstractions;
using MCPServer.Domain.Users.Enums;
using MCPServer.Domain.Users.ValueObjects;

namespace MCPServer.Domain.Users;

public class User : Entity<Guid>, IAggregateRoot
{
    public PhoneNumber PhoneNumber { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public DateTime? BirthDate { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsProfileCompleted { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public DateTime? LockedUntil { get; private set; }

    private User() { }

    private User(UserId id, PhoneNumber phoneNumber, string? firstName, string? lastName, DateTime? birthDate, bool isAdmin)
    {
        Id = id.Value;
        PhoneNumber = phoneNumber;
        FirstName = firstName?.Trim();
        LastName = lastName?.Trim();
        BirthDate = birthDate;
        IsAdmin = isAdmin;
        Status = UserStatus.PendingVerification;
        IsProfileCompleted = false;
        CreatedAt = DateTime.UtcNow;
    }

    public static User Register(
        PhoneNumber phoneNumber,
        string? firstName = null,
        string? lastName = null,
        DateTime? birthDate = null,
        bool isAdmin = false)
    {
        if (birthDate.HasValue && birthDate.Value > DateTime.UtcNow)
            throw new UserArgumentException(message: "Birth date cannot be in the future");

        if (birthDate.HasValue && birthDate.Value.Year < 1900)
            throw new UserArgumentException(message: "Birth date is invalid");

        var userId = UserId.New();
        var user = new User(userId, phoneNumber, firstName, lastName, birthDate, isAdmin);
        return user;
    }

    public void Verify()
    {
        if (Status != UserStatus.PendingVerification)
            throw new UserInvalidOperationException("User is not in pending verification status");

        Status = UserStatus.Active;
        VerifiedAt = DateTime.UtcNow;
    }

    public void SetAsAdmin(bool isAdmin)
    {
        IsAdmin = isAdmin;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (Status == UserStatus.Active)
            throw new UserInvalidOperationException("User is already activated");

        Status = UserStatus.Active;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (Status == UserStatus.Deactivated)
            throw new UserInvalidOperationException("User is already deactivated");

        Status = UserStatus.Deactivated;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Lock(TimeSpan lockoutDuration)
    {
        Status = UserStatus.Locked;
        LockedUntil = DateTime.UtcNow.Add(lockoutDuration);
        ModifiedAt = DateTime.UtcNow;
    }

    public void Unlock()
    {
        if (Status == UserStatus.Locked)
        {
            Status = UserStatus.Active;
            LockedUntil = null;
            ModifiedAt = DateTime.UtcNow;
        }
    }

    public bool IsLockedOut()
    {
        if (Status != UserStatus.Locked)
            return false;

        if (LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow)
            return true;

        Unlock();
        return false;
    }
}
