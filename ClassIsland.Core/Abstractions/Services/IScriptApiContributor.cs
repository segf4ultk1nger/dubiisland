namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// 脚本 API 贡献者。插件实现此接口并在 <c>PluginBase.Initialize</c> 中注册为单例后，
/// 即可向所有脚本引擎注册全局对象和函数。
/// </summary>
public interface IScriptApiContributor
{
    /// <summary>
    /// 配置要注册到脚本引擎的全局对象和函数。
    /// </summary>
    /// <param name="api">脚本 API 构建器。</param>
    void Configure(ScriptApiBuilder api);
}
