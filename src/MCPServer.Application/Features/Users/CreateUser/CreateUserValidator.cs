using MCPServer.Application.Abstractions;
using MCPServer.Application.Exceptions;
using static MCPServer.Application.Features.Users.Exceptions.BaseBusinessUserException;

namespace MCPServer.Application.Features.Users.CreateUser;

public class CreateUserValidator : BaseBusinessValidator
{
    private readonly CreateUserCommand _request;

    public CreateUserValidator(CreateUserCommand request)
    {
        _request = request;
    }

    public override async Task<List<BusinessException>> ValidateAsync()
    {
        var exceptions = new List<BusinessException>();

        if (string.IsNullOrWhiteSpace(_request.PhoneNumber))
        {
            exceptions.Add(new UserValidationException(message: "Invalid phone number"));
        }

        return exceptions;
    }
}