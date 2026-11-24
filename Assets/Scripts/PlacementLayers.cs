using UnityEngine;

/// <summary>
/// 放置相关的共享常量：网格可视化对象使用独立 Layer，避免射线误命中已放下的球。
/// </summary>
public static class PlacementLayers
{
    public const string MeshLayerName = "ARMesh";
    public const int MeshLayerIndex = 8;

    /// <summary>按名称解析网格层；名称不存在时退回约定的 Layer 8。</summary>
    public static int ResolveMeshLayer()
    {
        var index = LayerMask.NameToLayer(MeshLayerName);
        return index >= 0 ? index : MeshLayerIndex;
    }

    public static int ResolveMeshLayerMask()
    {
        return 1 << ResolveMeshLayer();
    }
}
