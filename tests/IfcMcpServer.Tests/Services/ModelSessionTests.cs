using IfcMcpServer.Services;
using IfcMcpServer.Tests;

namespace IfcMcpServer.Tests.Services;

public class ModelSessionTests : IDisposable
{
    private readonly ModelSession _session = new();

    public ModelSessionTests()
    {
        TestModelBuilder.EnsureTestModel();
    }

    [Fact]
    public void InitialState_NoModelLoaded()
    {
        Assert.False(_session.IsModelLoaded);
        Assert.Null(_session.CurrentModel);
        Assert.Null(_session.FilePath);
    }

    [Fact]
    public void OpenModel_LoadsIfc()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);

        Assert.True(_session.IsModelLoaded);
        Assert.NotNull(_session.CurrentModel);
        Assert.Equal(TestModelBuilder.TestModelPath, _session.FilePath);
    }

    [Fact]
    public void OpenModel_FileNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _session.OpenModel("nonexistent.ifc"));
    }

    [Fact]
    public void OpenModel_ReplacesExistingModel()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);
        var firstModel = _session.CurrentModel;

        _session.OpenModel(TestModelBuilder.TestModelPath);

        Assert.NotSame(firstModel, _session.CurrentModel);
    }

    [Fact]
    public void CloseModel_ClearsState()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _session.CloseModel();

        Assert.False(_session.IsModelLoaded);
        Assert.Null(_session.CurrentModel);
        Assert.Null(_session.FilePath);
    }

    [Fact]
    public void CloseModel_WhenNoneOpen_DoesNotThrow()
    {
        _session.CloseModel(); // should not throw
        Assert.False(_session.IsModelLoaded);
    }

    public void Dispose() => _session.Dispose();
}
