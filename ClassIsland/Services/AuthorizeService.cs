using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Controls.CommonDialog;
using ClassIsland.Models;
using ClassIsland.Models.Authorize;
using ClassIsland.Shared;
using ClassIsland.Views;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services;

public class AuthorizeService(ILogger<AuthorizeService> logger) : IAuthorizeService
{
    private int _failedAttempts;
    private DateTime _lockedUntil;

    private static Credential ConvertCredentialStringToModel(string credentialString)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(credentialString));
        return JsonSerializer.Deserialize<Credential>(json) ?? new Credential();
    }

    private static string ConvertCredentialModelToString(Credential credential)
    {
        var json = JsonSerializer.Serialize(credential);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public async Task<string?> SetupCredentialStringAsync(string? credentialString = null)
    {
        try
        {
            var credential = credentialString != null ? ConvertCredentialStringToModel(credentialString) : new Credential();
            var window = new AuthorizeWindow(credential, true);
            var result = window.ShowDialog();
            return result != true ? credentialString : ConvertCredentialModelToString(credential);
        }
        catch (Exception e)
        {
            logger.LogError(e, "创建认证信息时发生异常");
            CommonDialog.ShowError($"创建认证信息时发生异常：{e.Message}");
            return credentialString;
        }
    }

    public async Task<bool> AuthenticateAsync(string credentialString)
    {
        if (string.IsNullOrWhiteSpace(credentialString))
        {
            logger.LogWarning("传入了空的认证字符串，默认为认证通过。");
            return true;
        }

        var settings = IAppHost.TryGetService<SettingsService>()?.Settings;
        var management = IAppHost.TryGetService<IManagementService>();
        if ((management == null || !management.IsManagementEnabled) &&
            settings != null && !settings.IsSecurityAccessControlEnabled)
        {
            logger.LogInformation("本地访问控制已关闭，跳过身份认证。");
            return true;
        }

        if (DateTime.Now < _lockedUntil)
        {
            var remaining = (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds);
            logger.LogWarning("认证处于锁定状态，剩余 {RemainingSeconds} 秒。", remaining);
            CommonDialog.ShowError($"认证尝试次数过多，请在 {remaining} 秒后重试。");
            return false;
        }

        try
        {
            var credential = ConvertCredentialStringToModel(credentialString);
            var window = new AuthorizeWindow(credential, false);
            var result = window.ShowDialog();
            if (result == true)
            {
                _failedAttempts = 0;
                _lockedUntil = default;
                logger.LogInformation("认证成功。");
                return true;
            }

            RegisterFailure(settings);
            return false;
        }
        catch (Exception e)
        {
            logger.LogError(e, "认证时发生异常");
            CommonDialog.ShowError($"认证时发生异常：{e.Message}");
            RegisterFailure(settings);
            return false;
        }
    }

    private void RegisterFailure(Settings? settings)
    {
        _failedAttempts++;
        logger.LogWarning("认证失败，已连续失败 {FailedAttempts} 次。", _failedAttempts);

        var maxAttempts = settings?.SecurityMaxAuthAttempts ?? 0;
        if (maxAttempts <= 0 || _failedAttempts < maxAttempts)
        {
            return;
        }

        var lockoutSeconds = settings?.SecurityLockoutSeconds ?? 0;
        _lockedUntil = DateTime.Now.AddSeconds(lockoutSeconds);
        _failedAttempts = 0;
        logger.LogWarning("认证失败次数达到上限，已锁定 {LockoutSeconds} 秒。", lockoutSeconds);
        CommonDialog.ShowError($"认证失败次数过多，已锁定 {lockoutSeconds} 秒。");
    }
}
