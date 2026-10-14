using MCPServer.Application.Exceptions;
using MCPServer.Domain.Users;

namespace MCPServer.Application.Features.Users.Exceptions;

public abstract class BaseBusinessUserException : BusinessException
{
    public BaseBusinessUserException(string code, string message, List<string> details) : base(
       code: $"{nameof(User)}-{code}",
       message: message,
       details: details)
    { }

    public sealed class UserValidationException : BaseBusinessUserException
    {
        public UserValidationException(string message, List<string>? details = null) : base(code: "100", message: message, details: details ?? []) { }
    }
}