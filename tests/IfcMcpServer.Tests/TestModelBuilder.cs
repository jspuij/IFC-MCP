using Xbim.Ifc;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.QuantityResource;
using Xbim.Ifc4.ExternalReferenceResource;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.IO;

namespace IfcMcpServer.Tests;

public static class TestModelBuilder
{
    private static readonly string TestDataDir = Path.Combine(
        AppContext.BaseDirectory, "TestData");
    public static readonly string TestModelPath = Path.Combine(TestDataDir, "TestModel.ifc");

    private static readonly object Lock = new();
    private static bool _built;

    public static void EnsureTestModel()
    {
        lock (Lock)
        {
            if (_built && File.Exists(TestModelPath)) return;
            Directory.CreateDirectory(TestDataDir);
            BuildTestModel(TestModelPath);
            _built = true;
        }
    }

    private static void BuildTestModel(string path)
    {
        var creds = new XbimEditorCredentials
        {
            ApplicationDevelopersName = "Test",
            ApplicationFullName = "TestApp",
            ApplicationVersion = "1.0",
            EditorsFamilyName = "Test"
        };

        using var model = IfcStore.Create(creds, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using var txn = model.BeginTransaction("Create test model");

        // Project
        var project = model.Instances.New<IfcProject>(p =>
        {
            p.Name = "Test Project";
            p.UnitsInContext = model.Instances.New<Xbim.Ifc4.MeasureResource.IfcUnitAssignment>();
        });

        // Site
        var site = model.Instances.New<IfcSite>(s => s.Name = "Test Site");
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = project;
            r.RelatedObjects.Add(site);
        });

        // Building
        var building = model.Instances.New<IfcBuilding>(b => b.Name = "Test Building");
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = site;
            r.RelatedObjects.Add(building);
        });

        // Storeys
        var groundFloor = model.Instances.New<IfcBuildingStorey>(s =>
        {
            s.Name = "Ground Floor";
            s.Elevation = 0.0;
        });
        var firstFloor = model.Instances.New<IfcBuildingStorey>(s =>
        {
            s.Name = "First Floor";
            s.Elevation = 3.0;
        });
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = building;
            r.RelatedObjects.Add(groundFloor);
            r.RelatedObjects.Add(firstFloor);
        });

        // Wall 1 - external, classified
        var wall1 = model.Instances.New<IfcWall>(w => w.Name = "External Wall 1");
        AddPsetWallCommon(model, wall1, isExternal: true);
        AddWallQuantities(model, wall1, length: 5.0, height: 3.0, grossVolume: 2.025, netSideArea: 15.0);

        // Classification on wall1
        var classification = model.Instances.New<IfcClassification>(c => c.Name = "Uniclass");
        var classRef = model.Instances.New<IfcClassificationReference>(cr =>
        {
            cr.Identification = "Ss_20_05_28";
            cr.Name = "Gypsum block walls";
            cr.ReferencedSource = classification;
        });
        model.Instances.New<IfcRelAssociatesClassification>(r =>
        {
            r.RelatingClassification = classRef;
            r.RelatedObjects.Add(wall1);
        });

        // Wall 2 - internal
        var wall2 = model.Instances.New<IfcWall>(w => w.Name = "Internal Wall 1");
        AddPsetWallCommon(model, wall2, isExternal: false);
        AddWallQuantities(model, wall2, length: 4.0, height: 3.0, grossVolume: 1.62, netSideArea: 12.0);

        // Slab
        var slab = model.Instances.New<IfcSlab>(s => s.Name = "Ground Floor Slab");
        AddSlabQuantities(model, slab, grossArea: 50.0, grossVolume: 10.0);

        // Door
        var door = model.Instances.New<IfcDoor>(d => d.Name = "Door 1");

        // Contain elements in Ground Floor
        model.Instances.New<IfcRelContainedInSpatialStructure>(r =>
        {
            r.RelatingStructure = groundFloor;
            r.RelatedElements.Add(wall1);
            r.RelatedElements.Add(wall2);
            r.RelatedElements.Add(slab);
            r.RelatedElements.Add(door);
        });

        txn.Commit();
        model.SaveAs(path);
    }

    private static void AddPsetWallCommon(IModel model, IfcWall wall, bool isExternal)
    {
        var pset = model.Instances.New<IfcPropertySet>(ps =>
        {
            ps.Name = "Pset_WallCommon";
            ps.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
            {
                p.Name = "IsExternal";
                p.NominalValue = new IfcBoolean(isExternal);
            }));
            ps.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
            {
                p.Name = "FireRating";
                p.NominalValue = new IfcLabel(isExternal ? "REI60" : "REI30");
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = pset;
            r.RelatedObjects.Add(wall);
        });
    }

    private static void AddWallQuantities(IModel model, IfcWall wall,
        double length, double height, double grossVolume, double netSideArea)
    {
        var qset = model.Instances.New<IfcElementQuantity>(eq =>
        {
            eq.Name = "Qto_WallBaseQuantities";
            eq.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
            {
                q.Name = "Length";
                q.LengthValue = length;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
            {
                q.Name = "Height";
                q.LengthValue = height;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityVolume>(q =>
            {
                q.Name = "GrossVolume";
                q.VolumeValue = grossVolume;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
            {
                q.Name = "NetSideArea";
                q.AreaValue = netSideArea;
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = qset;
            r.RelatedObjects.Add(wall);
        });
    }

    private static void AddSlabQuantities(IModel model, IfcSlab slab,
        double grossArea, double grossVolume)
    {
        var qset = model.Instances.New<IfcElementQuantity>(eq =>
        {
            eq.Name = "Qto_SlabBaseQuantities";
            eq.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
            {
                q.Name = "GrossArea";
                q.AreaValue = grossArea;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityVolume>(q =>
            {
                q.Name = "GrossVolume";
                q.VolumeValue = grossVolume;
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = qset;
            r.RelatedObjects.Add(slab);
        });
    }
}
