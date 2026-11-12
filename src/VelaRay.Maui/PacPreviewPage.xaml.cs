namespace VelaRay.Maui;

public partial class PacPreviewPage : ContentPage
{
    private readonly string _script;
    public PacPreviewPage(string script)
    {
        InitializeComponent();
        _script = script;
        PacPreviewEditor.Text = script;
    }
    private async void CopyClicked(object sender, EventArgs e) => await Clipboard.Default.SetTextAsync(_script);
    private async void CloseClicked(object sender, EventArgs e) => await Navigation.PopModalAsync();
}
