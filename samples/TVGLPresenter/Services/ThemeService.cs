using Microsoft.FluentUI.AspNetCore.Components;

namespace TVGLPresenter.Services;

public class ThemeService
{
    private bool _isDarkMode = false;

    public event Action? OnThemeChanged;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                OnThemeChanged?.Invoke();
            }
        }
    }

    public DesignThemeModes CurrentMode => _isDarkMode ? DesignThemeModes.Dark : DesignThemeModes.Light;
}
