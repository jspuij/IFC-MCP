using Xbim.Ifc;

namespace IfcMcpServer.Services;

public class ModelSession : IDisposable
{
    public IfcStore? CurrentModel { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsModelLoaded => CurrentModel != null;

    public void OpenModel(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"IFC file not found: {filePath}", filePath);

        CloseModel();
        CurrentModel = IfcStore.Open(filePath);
        FilePath = filePath;
    }

    public void CloseModel()
    {
        CurrentModel?.Dispose();
        CurrentModel = null;
        FilePath = null;
    }

    public void Dispose()
    {
        CloseModel();
        GC.SuppressFinalize(this);
    }
}
