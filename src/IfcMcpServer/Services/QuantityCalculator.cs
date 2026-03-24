using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public record QuantityGroup(string GroupKey, int ElementCount, Dictionary<string, double> Quantities);
public record QuantityResult(IReadOnlyList<string> QuantityColumns, IReadOnlyList<QuantityGroup> Groups);

public class QuantityCalculator
{
    private readonly ElementQueryService _queryService;

    public QuantityCalculator(ElementQueryService queryService)
    {
        _queryService = queryService;
    }

    public QuantityResult Calculate(
        IModel model,
        string? ifcType,
        string? classification,
        string[]? propertyFilters,
        string groupBy,
        string[]? quantityNames)
    {
        var elements = _queryService.QueryElements(model, ifcType, classification, propertyFilters).ToList();
        var groups = GroupElements(elements, groupBy, model);
        var allQuantityNames = new HashSet<string>();
        var groupResults = new List<QuantityGroup>();

        foreach (var (groupKey, groupElements) in groups)
        {
            var sums = new Dictionary<string, double>();

            foreach (var element in groupElements)
            {
                var quantities = ResolveQuantities(element);
                foreach (var (name, value) in quantities)
                {
                    if (quantityNames != null && !quantityNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    allQuantityNames.Add(name);
                    sums[name] = sums.GetValueOrDefault(name, 0) + value;
                }
            }

            groupResults.Add(new QuantityGroup(groupKey, groupElements.Count, sums));
        }

        var columns = (quantityNames ?? allQuantityNames.OrderBy(n => n).ToArray()).ToList();
        return new QuantityResult(columns, groupResults);
    }

    private static IEnumerable<(string GroupKey, List<IIfcProduct> Elements)> GroupElements(
        List<IIfcProduct> elements, string groupBy, IModel model)
    {
        if (groupBy.StartsWith("property:", StringComparison.OrdinalIgnoreCase))
        {
            var propPath = groupBy["property:".Length..];
            return elements
                .GroupBy(e => GetPropertyValue(e, propPath) ?? "(no value)")
                .Select(g => (g.Key, g.ToList()));
        }

        return groupBy.ToLowerInvariant() switch
        {
            "type" => elements
                .GroupBy(e => e.GetType().Name)
                .Select(g => (g.Key, g.ToList())),

            "classification" => elements
                .GroupBy(e =>
                {
                    var classRef = ElementQueryService.GetClassificationReferences(e).FirstOrDefault();
                    return classRef?.Identification?.ToString() ?? "(unclassified)";
                })
                .Select(g => (g.Key, g.ToList())),

            "storey" => elements
                .GroupBy(e => ElementQueryService.GetStoreyName(e) ?? "(no storey)")
                .Select(g => (g.Key, g.ToList())),

            _ => throw new ArgumentException($"Unknown groupBy value: '{groupBy}'. Expected: type, classification, storey, or property:PsetName.PropertyName")
        };
    }

    private static IEnumerable<(string Name, double Value)> ResolveQuantities(IIfcProduct element)
    {
        var resolved = new Dictionary<string, double>();

        // Type-level quantities first (lower priority)
        var typeObj = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeObj != null)
        {
            foreach (var qset in typeObj.HasPropertySets.OfType<IIfcElementQuantity>())
            {
                foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                {
                    var val = GetNumericValue(q);
                    if (val.HasValue)
                        resolved[q.Name.ToString()] = val.Value;
                }
            }
        }

        // Instance-level quantities (higher priority — overwrites type-level)
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcElementQuantity qset)
            {
                foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                {
                    var val = GetNumericValue(q);
                    if (val.HasValue)
                        resolved[q.Name.ToString()] = val.Value;
                }
            }
        }

        return resolved.Select(kv => (kv.Key, kv.Value));
    }

    private static double? GetNumericValue(IIfcPhysicalSimpleQuantity q) => q switch
    {
        IIfcQuantityLength ql => ql.LengthValue,
        IIfcQuantityArea qa => qa.AreaValue,
        IIfcQuantityVolume qv => qv.VolumeValue,
        IIfcQuantityWeight qw => qw.WeightValue,
        IIfcQuantityCount qc => qc.CountValue,
        _ => null
    };

    private static string? GetPropertyValue(IIfcProduct element, string propPath)
    {
        var dotIdx = propPath.IndexOf('.');
        if (dotIdx < 0) return null;

        var psetName = propPath[..dotIdx];
        var propName = propPath[(dotIdx + 1)..];

        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset &&
                string.Equals(pset.Name?.ToString(), psetName, StringComparison.OrdinalIgnoreCase))
            {
                var prop = pset.HasProperties
                    .OfType<IIfcPropertySingleValue>()
                    .FirstOrDefault(p => string.Equals(p.Name.ToString(), propName, StringComparison.OrdinalIgnoreCase));
                return prop?.NominalValue?.ToString();
            }
        }
        return null;
    }
}
