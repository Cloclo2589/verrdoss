using System.Windows;

namespace Verrdoss.App;

public partial class PasswordWindow : Window
{
    public PasswordWindow(string title, string prompt, bool confirm = false)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        if (confirm)
        {
            ConfirmLabel.Visibility = Visibility.Visible;
            ConfirmInput.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string Password => PasswordInput.Password;
    public string Confirmation => ConfirmInput.Password;

    public void Clear()
    {
        PasswordInput.Password = "";
        ConfirmInput.Password = "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (ConfirmInput.Visibility == Visibility.Visible && PasswordInput.Password != ConfirmInput.Password)
        {
            MessageBox.Show("Les deux saisies ne correspondent pas.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Clear();
        DialogResult = false;
    }
}
