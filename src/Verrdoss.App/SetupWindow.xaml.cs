using System.Windows;

namespace Verrdoss.App;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string Password => PasswordInput.Password;

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length < 8)
        {
            MessageBox.Show("Le mot de passe maître doit contenir au moins 8 caractères.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (PasswordInput.Password != ConfirmInput.Password)
        {
            MessageBox.Show("Les deux saisies ne correspondent pas.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
