using SwiftlyS2.Shared.Commands;

namespace CS2_Admin.Commands;

public interface ICommand
{
    void Execute(ICommandContext context);
}
