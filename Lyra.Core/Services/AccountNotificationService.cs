namespace Lyra.Core.Services;

public class AccountNotificationService
{
    public event Func<Task>? OnAccountsChanged;

    public async Task NotifyAccountsChangedAsync()
    {
        if (OnAccountsChanged != null)
            await OnAccountsChanged.Invoke();
    }
}