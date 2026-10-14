using MCPServer.Domain.Exceptions;

namespace MCPServer.Domain.Users;

public class BaseUserException : DomainException
{
    public BaseUserException(string code, string message, List<string> details) : base(
        code: $"{nameof(User)}-{code}",
        message: message,
        details: details)
    { }
}


public sealed class InvalidPhoneException : BaseUserException
{
    public InvalidPhoneException() : base(code: "100", message: "Invalid phone number", details: []) { }
    public InvalidPhoneException(string message, List<string> details) : base(code: "100", message: message, details: details) { }
}

public sealed class UserArgumentException : BaseUserException
{
    public UserArgumentException(string message) : base(code: "101", message: message, details: []) { }
}

public sealed class UserInvalidOperationException : BaseUserException
{
    public UserInvalidOperationException(string message) : base(code: "102", message: message, details: []) { }
}