using System;
using System.Windows;
using System.Windows.Media;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛内可自绘组件。宿主给组件分配一个矩形槽位，组件自行用 <see cref="DrawingContext"/> 绘制。
/// </summary>
public interface IIslandComponent
{
    /// <summary>所在行号（组件配置中行的索引，由宿主在构建时设置）。</summary>
    int LineNumber { get; set; }

    /// <summary>为 false 时本帧不参与布局与绘制。</summary>
    bool IsVisible { get; }

    /// <summary>测量组件所需尺寸，用于横向排布。</summary>
    Size Measure(Size availableSize, IslandContext context);

    /// <summary>在给定槽位内自绘。</summary>
    void Render(DrawingContext drawingContext, Rect slot, IslandContext context);

    /// <summary>组件内容变化时触发，促使岛重绘。</summary>
    event EventHandler? Invalidated;
}
