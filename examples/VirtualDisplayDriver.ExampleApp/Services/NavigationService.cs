using CommunityToolkit.Mvvm.ComponentModel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private object? _currentViewModel;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<T>() where T : class
        => NavigateTo(typeof(T));

    public void NavigateTo(Type viewModelType)
    {
        CurrentViewModel = _serviceProvider.GetService(viewModelType);
    }
}
