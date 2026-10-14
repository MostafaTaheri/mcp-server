using MCPServer.Application.Exceptions;
using MCPServer.Application.Features.Users.CreateUser;
using MCPServer.Domain;
using MCPServer.Domain.Users;
using MCPServer.Domain.Users.ValueObjects;
using static MCPServer.Application.Features.Users.Exceptions.BaseBusinessUserException;

namespace MCPServer.Application.Users.Backoffice.CreateUser;

public class CreateUserService : ICreateUserService
{
    private readonly IMCPServerUnitOfWork _unitOfWork;

    public CreateUserService(IMCPServerUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User> CreateUserAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        await new CreateUserValidator(command).AssertValidationAsync();

        var phoneNumber = PhoneNumber.Create(command.PhoneNumber);
        var user = User.Register(phoneNumber);

         try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            await _unitOfWork.Users.AddAsync(user, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw new UserValidationException("an error occoured");
        }

        return user;
    }
}
