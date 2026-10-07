using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;

namespace ClassIsland.Controls.AuthorizeProvider;

/// <summary>
/// TotpAuthorizeProvider.xaml 的交互逻辑
/// </summary>
[AuthorizeProviderInfo("classisland.authProviders.totp", "TOTP 验证器", "")]
public partial class TotpAuthorizeProvider
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int Digits = 6;
    private const int Period = 30;

    public static readonly DependencyProperty AuthorizeFailedProperty = DependencyProperty.Register(
        nameof(AuthorizeFailed), typeof(bool), typeof(TotpAuthorizeProvider), new PropertyMetadata(default(bool)));

    public bool AuthorizeFailed
    {
        get { return (bool)GetValue(AuthorizeFailedProperty); }
        set { SetValue(AuthorizeFailedProperty, value); }
    }

    public TotpAuthorizeProvider()
    {
        InitializeComponent();
    }

    private void TotpAuthorizeProvider_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsEditingMode && string.IsNullOrEmpty(Settings.Secret))
        {
            GenerateSecret();
        }
        RefreshSecretDisplay();

        var parentItem = VisualTreeUtils.FindParentVisuals<ListBoxItem>(this).FirstOrDefault();
        if (parentItem?.IsSelected == true)
        {
            CodeBox.Focus();
        }
    }

    private void ButtonRegenerateSecret_OnClick(object sender, RoutedEventArgs e)
    {
        GenerateSecret();
        RefreshSecretDisplay();
        VerifyStatusText.Visibility = Visibility.Collapsed;
        AuthorizeFailed = false;
    }

    private void ButtonVerify_OnClick(object sender, RoutedEventArgs e)
    {
        Verify();
    }

    private void CodeBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Verify();
        }
    }

    private void Verify()
    {
        var success = !string.IsNullOrEmpty(Settings.Secret) && VerifyCode(Settings.Secret, CodeBox.Text);
        AuthorizeFailed = !success;
        VerifyStatusText.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        if (success)
        {
            VerifyStatusText.Text = "验证通过。";
            CompleteAuthorize();
        }
    }

    private void GenerateSecret()
    {
        Settings.Secret = Base32Encode(FrameworkCompat.GetRandomBytes(20));
    }

    private void RefreshSecretDisplay()
    {
        if (!IsEditingMode)
        {
            return;
        }
        SecretBox.Text = GroupSecret(Settings.Secret);
        UriBox.Text = $"otpauth://totp/LegacyIsland?secret={Settings.Secret}&issuer=LegacyIsland";
    }

    private static string GroupSecret(string secret)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < secret.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }
            builder.Append(secret[i]);
        }
        return builder.ToString();
    }

    private static bool VerifyCode(string secret, string input)
    {
        input = (input ?? "").Trim().Replace(" ", "");
        if (input.Length != Digits || !input.All(char.IsDigit))
        {
            return false;
        }

        var key = Base32Decode(secret);
        if (key.Length == 0)
        {
            return false;
        }

        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Period;
        for (var offset = -1; offset <= 1; offset++)
        {
            if (FixedTimeEquals(ComputeTotp(key, step + offset), input))
            {
                return true;
            }
        }
        return false;
    }

    private static string ComputeTotp(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[hash.Length - 1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        return (binary % (int)Math.Pow(10, Digits)).ToString("D" + Digits);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static string Base32Encode(byte[] data)
    {
        var builder = new StringBuilder();
        int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            builder.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        }
        return builder.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        input = (input ?? "").Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        var output = new System.Collections.Generic.List<byte>();
        int bits = 0, value = 0;
        foreach (var c in input)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                continue;
            }
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
