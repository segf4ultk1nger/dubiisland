namespace ClassIsland.Controls.Island;

/// <summary>组件被移除/重建时需要释放非托管订阅（渲染帧、计时器）时实现。</summary>
public interface IIslandComponentCleanup
{
    void Cleanup();
}
