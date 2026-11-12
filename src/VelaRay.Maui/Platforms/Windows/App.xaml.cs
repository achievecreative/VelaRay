using Microsoft.UI.Xaml;

namespace VelaRay.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();
    protected override MauiApp CreateMauiApp() => VelaRay.Maui.MauiProgram.CreateMauiApp();
}
