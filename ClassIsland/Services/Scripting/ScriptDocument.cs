using Jint;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 表示一个已加载并处于运行状态的脚本文档。
/// </summary>
public class ScriptDocument
{
    /// <summary>
    /// 脚本文件路径。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 脚本对应的 Jint 引擎实例。
    /// </summary>
    public Engine Engine { get; }

    /// <summary>
    /// 初始化一个 <see cref="ScriptDocument"/> 实例。
    /// </summary>
    /// <param name="filePath">脚本文件路径。</param>
    /// <param name="engine">脚本对应的 Jint 引擎实例。</param>
    public ScriptDocument(string filePath, Engine engine)
    {
        FilePath = filePath;
        Engine = engine;
    }
}
