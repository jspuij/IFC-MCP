using System.ComponentModel;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class ViewerTools
{
    [McpServerTool(Name = "viewer-open", ReadOnly = false),
     Description("Start the 3D web viewer and return the URL. Opens in a browser to visualize the currently loaded IFC model.")]
    public static async Task<string> ViewerOpen(
        ModelSession session,
        ViewerService viewer)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        await viewer.StartAsync();
        return $"Viewer started at {viewer.Url}\nOpen this URL in a browser to see the 3D model.";
    }

    [McpServerTool(Name = "viewer-close", ReadOnly = false),
     Description("Stop the 3D web viewer.")]
    public static async Task<string> ViewerClose(ViewerService viewer)
    {
        if (!viewer.IsRunning)
            return "Viewer is not running.";

        await viewer.StopAsync();
        return "Viewer stopped.";
    }

    [McpServerTool(Name = "viewer-highlight", ReadOnly = true),
     Description("Highlight specific elements in the 3D viewer by their GlobalId. Other elements are dimmed.")]
    public static async Task<string> ViewerHighlight(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to highlight")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendHighlightAsync(globalIds);
        return $"Highlighted {globalIds.Length} element(s) in the viewer.";
    }

    [McpServerTool(Name = "viewer-isolate", ReadOnly = true),
     Description("Isolate specific elements in the 3D viewer — hides everything except the specified elements.")]
    public static async Task<string> ViewerIsolate(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to isolate")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendIsolateAsync(globalIds);
        return $"Isolated {globalIds.Length} element(s) in the viewer.";
    }

    [McpServerTool(Name = "viewer-reset", ReadOnly = true),
     Description("Reset the 3D viewer to show all elements with default visibility and appearance.")]
    public static async Task<string> ViewerReset(ViewerService viewer)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendResetAsync();
        return "Viewer reset to default state.";
    }

    [McpServerTool(Name = "viewer-camera", ReadOnly = true),
     Description("Fly the camera to fit specific elements in the 3D viewer.")]
    public static async Task<string> ViewerCamera(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to fit in the camera view")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendCameraFitAsync(globalIds);
        return $"Camera fitted to {globalIds.Length} element(s).";
    }
}
