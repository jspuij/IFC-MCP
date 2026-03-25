using System.ComponentModel;
using System.Text;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class ModelTools
{
    [McpServerTool(Name = "open-model", ReadOnly = false), Description("Open an IFC file for querying. Closes any previously opened model.")]
    public static async Task<string> OpenModel(
        ModelSession session,
        ViewerService viewer,
        [Description("Absolute or relative path to the IFC file")] string filePath)
    {
        session.OpenModel(filePath);
        var model = session.CurrentModel!;

        var schemaVersion = model.SchemaVersion.ToString();
        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        var projectName = project?.Name?.ToString() ?? "(unnamed)";
        var totalElements = model.Instances.OfType<IIfcProduct>().Count();

        if (viewer.IsRunning)
            await viewer.SendReloadAsync();

        return $"Model opened: {filePath}\nSchema: {schemaVersion}\nProject: {projectName}\nTotal elements: {totalElements}";
    }

    [McpServerTool(Name = "close-model", ReadOnly = false), Description("Close the currently loaded IFC model and free memory.")]
    public static async Task<string> CloseModel(
        ModelSession session,
        ViewerService viewer)
    {
        if (!session.IsModelLoaded)
            return "No model is currently loaded.";

        if (viewer.IsRunning)
            await viewer.StopAsync();

        session.CloseModel();
        return "Model closed.";
    }

    [McpServerTool(Name = "model-info", ReadOnly = true), Description("Get detailed information about the currently loaded IFC model including spatial hierarchy.")]
    public static string ModelInfo(ModelSession session)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var model = session.CurrentModel!;
        var sb = new StringBuilder();

        sb.AppendLine($"File: {session.FilePath}");
        sb.AppendLine($"Schema: {model.SchemaVersion}");

        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        sb.AppendLine($"Project: {project?.Name?.ToString() ?? "(unnamed)"}");

        foreach (var site in model.Instances.OfType<IIfcSite>())
        {
            sb.AppendLine($"\nSite: {site.Name?.ToString() ?? "(unnamed)"}");

            var buildings = site.IsDecomposedBy
                .SelectMany(r => r.RelatedObjects)
                .OfType<IIfcBuilding>();

            foreach (var building in buildings)
            {
                sb.AppendLine($"  Building: {building.Name?.ToString() ?? "(unnamed)"}");

                var storeys = building.IsDecomposedBy
                    .SelectMany(r => r.RelatedObjects)
                    .OfType<IIfcBuildingStorey>()
                    .OrderBy(s => s.Elevation?.Value ?? 0);

                foreach (var storey in storeys)
                {
                    var elementCount = storey.ContainsElements
                        .SelectMany(r => r.RelatedElements)
                        .Count();
                    sb.AppendLine($"    Storey: {storey.Name?.ToString() ?? "(unnamed)"} (elevation: {storey.Elevation?.Value ?? 0:F1}, elements: {elementCount})");
                }
            }
        }

        return sb.ToString();
    }
}
